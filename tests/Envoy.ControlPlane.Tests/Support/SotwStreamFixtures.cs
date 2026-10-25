using System.Collections.Concurrent;
using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Xunit;

namespace Envoy.ControlPlane.Tests.Support;

internal static class StreamFixtures
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    internal static Task Wait(Task task) => task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    internal static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static DiscoveryRequest Request(string type = "type/A", string nonce = "", params string[] names)
    {
        var request = new DiscoveryRequest { TypeUrl = type, ResponseNonce = nonce };
        request.ResourceNames.Add(names);
        return request;
    }
    internal static TestSotwResponse Response(DiscoveryRequest request, string version = "v1", string[]? names = null,
        ResponseContext context = default, DiscoveryResponse? body = null) => new(request, version,
            (names ?? []).ToDictionary(name => name, _ => version, StringComparer.Ordinal), context,
            () => body ?? new DiscoveryResponse { VersionInfo = version, TypeUrl = request.TypeUrl });
}

internal sealed class StreamInput : IAsyncEnumerable<DiscoveryRequest>, IAsyncEnumerator<DiscoveryRequest>
{
    private readonly Channel<DiscoveryRequest> requests = Channel.CreateUnbounded<DiscoveryRequest>();
    private CancellationToken cancellationToken;
    private int readAttempts;
    internal int ReadAttempts => Volatile.Read(ref readAttempts);
    internal int Disposals { get; private set; }
    internal Exception? CreationFailure { get; init; }
    internal Exception? DisposalFailure { get; init; }
    public DiscoveryRequest Current { get; private set; } = null!;

    internal void Push(DiscoveryRequest request) => Assert.True(requests.Writer.TryWrite(request));
    internal void Complete(Exception? failure = null) => requests.Writer.TryComplete(failure);

    public IAsyncEnumerator<DiscoveryRequest> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (CreationFailure is not null)
        {
            throw CreationFailure;
        }
        this.cancellationToken = cancellationToken;
        return this;
    }

    public async ValueTask<bool> MoveNextAsync()
    {
        Interlocked.Increment(ref readAttempts);
        try
        {
            Current = await requests.Reader.ReadAsync(cancellationToken);
            return true;
        }
        catch (ChannelClosedException error) when (error.InnerException is null)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return DisposalFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposalFailure);
    }
}

internal sealed class StreamWatch(ChannelWriter<ISotwResponse> responses) : IWatch<ISotwResponse>
{
    private readonly TaskCompletionSource<ISotwResponse> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Disposals { get; private set; }
    internal Exception? DisposalFailure { get; init; }
    public Task<ISotwResponse> Completion => completion.Task;
    internal void Respond(ISotwResponse response)
    {
        Assert.True(responses.TryWrite(response));
        Assert.True(completion.TrySetResult(response));
    }
    internal void Fail(Exception failure)
    {
        Assert.True(responses.TryComplete(failure));
        Assert.True(completion.TrySetException(failure));
    }
    internal void Cancel()
    {
        Assert.True(responses.TryComplete(new OperationCanceledException("watch canceled")));
        Assert.True(completion.TrySetCanceled());
    }
    public void Dispose()
    {
        Disposals++;
        completion.TrySetCanceled();
        if (DisposalFailure is not null)
        {
            throw DisposalFailure;
        }
    }
}

internal sealed record StreamRegistration(DiscoveryRequest Request, ISubscription Subscription, StreamWatch Watch);

internal sealed class StreamWatcher : IConfigWatcher
{
    internal Channel<StreamRegistration> Created { get; } = Channel.CreateUnbounded<StreamRegistration>();
    internal ConcurrentQueue<StreamRegistration> Registrations { get; } = new();
    internal Func<DiscoveryRequest, ISubscription, ChannelWriter<ISotwResponse>, StreamWatch>? Create { get; set; }
    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses)
    {
        var watch = Create?.Invoke(request, subscription, responses) ?? new StreamWatch(responses);
        var registration = new StreamRegistration(request, subscription, watch);
        Registrations.Enqueue(registration);
        Assert.True(Created.Writer.TryWrite(registration));
        return watch;
    }
    public IWatch<IDeltaResponse> CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription) => throw new NotSupportedException();
}

internal sealed class StreamCallbacks : ISotwCallbacks
{
    internal Func<ResponseContext, long, string, CancellationToken, ValueTask> Open { get; set; } = (_, _, _, _) => ValueTask.CompletedTask;
    internal Func<long, DiscoveryRequest, CancellationToken, ValueTask> Request { get; set; } = (_, _, _) => ValueTask.CompletedTask;
    internal Func<ResponseContext, long, DiscoveryRequest, DiscoveryResponse, CancellationToken, ValueTask> Response { get; set; } = (_, _, _, _, _) => ValueTask.CompletedTask;
    internal Func<long, Node, ValueTask> Close { get; set; } = (_, _) => ValueTask.CompletedTask;
    public ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken) => Open(context, streamId, typeUrl, cancellationToken);
    public ValueTask OnStreamRequestAsync(long streamId, DiscoveryRequest request, CancellationToken cancellationToken) => Request(streamId, request, cancellationToken);
    public ValueTask OnStreamResponseAsync(ResponseContext context, long streamId, DiscoveryRequest request, DiscoveryResponse response, CancellationToken cancellationToken) => Response(context, streamId, request, response, cancellationToken);
    public ValueTask OnStreamClosedAsync(long streamId, Node node) => Close(streamId, node);
}

internal sealed class StreamHarness : IAsyncDisposable
{
    internal StreamInput Input { get; }
    internal StreamWatcher Watcher { get; } = new();
    internal StreamCallbacks Callbacks { get; } = new();
    internal Channel<DiscoveryResponse> Sent { get; } = Channel.CreateUnbounded<DiscoveryResponse>();
    internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    internal Func<DiscoveryResponse, CancellationToken, ValueTask>? Sending { get; set; }
    internal Task Running { get; private set; } = Task.CompletedTask;
    internal StreamHarness(StreamInput? input = null) => Input = input ?? new StreamInput();

    internal void Start(string typeUrl = "type/A", IConfigWatcher? cache = null, SotwOptions? options = null,
        object? state = null, CancellationTokenSource? host = null)
    {
        var server = new SotwServer(cache ?? Watcher, Callbacks, options, host?.Token ?? default);
        Running = server.StreamAsync(Input, SendAsync, typeUrl, new ResponseContext(Cancellation.Token, state));
    }
    private async ValueTask SendAsync(DiscoveryResponse response, CancellationToken token)
    {
        Assert.True(Sent.Writer.TryWrite(response));
        if (Sending is not null)
        {
            await Sending(response, token);
        }
    }
    internal Task<StreamRegistration> NextWatch() => StreamFixtures.Wait(Watcher.Created.Reader.ReadAsync().AsTask());
    internal Task<DiscoveryResponse> NextResponse() => StreamFixtures.Wait(Sent.Reader.ReadAsync().AsTask());
    internal async Task Finish()
    {
        Input.Complete();
        await StreamFixtures.Wait(Running);
    }
    public async ValueTask DisposeAsync()
    {
        Cancellation.Cancel();
        Input.Complete();
        try
        {
            await StreamFixtures.Wait(Running);
        }
        catch (Exception error) when (Running.IsCompleted)
        {
            // Each failure test observes its result; cleanup must preserve the test's primary assertion.
            TestContext.Current.TestOutputHelper?.WriteLine("Stream cleanup observed: " + error.Message);
        }
        Cancellation.Dispose();
    }
}
