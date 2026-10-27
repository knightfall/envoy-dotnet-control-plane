using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Xunit;

namespace Envoy.ControlPlane.Tests.Support;

internal static class DeltaStreamFixtures
{
    internal static DeltaDiscoveryRequest Request(string type = "type/A", string nonce = "", string[]? additions = null,
        string[]? removals = null)
    {
        var request = new DeltaDiscoveryRequest { TypeUrl = type, ResponseNonce = nonce };
        request.ResourceNamesSubscribe.Add(additions ?? []);
        request.ResourceNamesUnsubscribe.Add(removals ?? []);
        return request;
    }

    internal static IDeltaResponse Response(DeltaDiscoveryRequest request, string version = "v1", params string[] names) =>
        new PassthroughDeltaResponse(request, new DeltaDiscoveryResponse { TypeUrl = request.TypeUrl, SystemVersionInfo = version },
            names.ToDictionary(name => name, _ => version, StringComparer.Ordinal));
}

internal sealed class DeltaInput : IAsyncEnumerable<DeltaDiscoveryRequest>, IAsyncEnumerator<DeltaDiscoveryRequest>
{
    private readonly Channel<DeltaDiscoveryRequest> requests = Channel.CreateUnbounded<DeltaDiscoveryRequest>();
    private CancellationToken cancellationToken;
    internal int Disposals { get; private set; }
    internal Exception? DisposalFailure { get; set; }
    public DeltaDiscoveryRequest Current { get; private set; } = null!;
    internal void Push(DeltaDiscoveryRequest request) => Assert.True(requests.Writer.TryWrite(request));
    internal void Complete(Exception? failure = null) => requests.Writer.TryComplete(failure);
    public IAsyncEnumerator<DeltaDiscoveryRequest> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        this.cancellationToken = cancellationToken;
        return this;
    }
    public async ValueTask<bool> MoveNextAsync()
    {
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

internal sealed class DeltaRegistration(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses) : IDisposable
{
    internal DeltaDiscoveryRequest Request { get; } = request;
    internal ISubscription Subscription { get; } = subscription;
    internal ChannelWriter<IDeltaResponse> Responses { get; } = responses;
    internal int Disposals { get; private set; }
    internal void Respond(IDeltaResponse response) => Assert.True(Responses.TryWrite(response));
    public void Dispose() => Disposals++;
}

internal sealed class DeltaWatcher : IConfigWatcher
{
    internal Channel<DeltaRegistration> Created { get; } = Channel.CreateUnbounded<DeltaRegistration>();
    internal List<DeltaRegistration> Registrations { get; } = [];
    internal Func<DeltaDiscoveryRequest, ISubscription, ChannelWriter<IDeltaResponse>, IDisposable>? Create { get; set; }
    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
    {
        var registration = new DeltaRegistration(request, subscription, responses);
        var owned = Create?.Invoke(request, subscription, responses) ?? registration;
        Registrations.Add(registration);
        Assert.True(Created.Writer.TryWrite(registration));
        return owned;
    }
    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses) =>
        throw new NotSupportedException();
}

internal sealed class DeltaCallbacks : IDeltaCallbacks
{
    internal Func<ResponseContext, long, string, CancellationToken, ValueTask> Open { get; set; } = (_, _, _, _) => ValueTask.CompletedTask;
    internal Func<long, DeltaDiscoveryRequest, CancellationToken, ValueTask> Request { get; set; } = (_, _, _) => ValueTask.CompletedTask;
    internal Func<long, DeltaDiscoveryRequest, DeltaDiscoveryResponse, CancellationToken, ValueTask> Response { get; set; } = (_, _, _, _) => ValueTask.CompletedTask;
    internal Func<long, Node, ValueTask> Close { get; set; } = (_, _) => ValueTask.CompletedTask;
    public ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken) => Open(context, streamId, typeUrl, cancellationToken);
    public ValueTask OnStreamRequestAsync(long streamId, DeltaDiscoveryRequest request, CancellationToken cancellationToken) => Request(streamId, request, cancellationToken);
    public ValueTask OnStreamResponseAsync(long streamId, DeltaDiscoveryRequest request, DeltaDiscoveryResponse response, CancellationToken cancellationToken) => Response(streamId, request, response, cancellationToken);
    public ValueTask OnStreamClosedAsync(long streamId, Node node) => Close(streamId, node);
}

internal sealed class DeltaHarness : IAsyncDisposable
{
    internal DeltaInput Input { get; } = new();
    internal DeltaWatcher Watcher { get; } = new();
    internal DeltaCallbacks Callbacks { get; } = new();
    internal Channel<DeltaDiscoveryResponse> Sent { get; } = Channel.CreateUnbounded<DeltaDiscoveryResponse>();
    internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    internal Func<DeltaDiscoveryResponse, CancellationToken, ValueTask>? Sending { get; set; }
    internal Task Running { get; private set; } = Task.CompletedTask;
    internal void Start(string typeUrl = "type/A", DeltaOptions? options = null, object? state = null, CancellationTokenSource? host = null)
    {
        var server = new DeltaServer(Watcher, Callbacks, options, host?.Token ?? default);
        Running = server.StreamAsync(Input, SendAsync, typeUrl, new ResponseContext(Cancellation.Token, state));
    }
    private async ValueTask SendAsync(DeltaDiscoveryResponse response, CancellationToken token)
    {
        Assert.True(Sent.Writer.TryWrite(response));
        if (Sending is not null)
        {
            await Sending(response, token);
        }
    }
    internal Task<DeltaRegistration> NextWatch() => StreamFixtures.Wait(Watcher.Created.Reader.ReadAsync().AsTask());
    internal Task<DeltaDiscoveryResponse> NextResponse() => StreamFixtures.Wait(Sent.Reader.ReadAsync().AsTask());
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
            TestContext.Current.TestOutputHelper?.WriteLine("Delta cleanup observed: " + error.Message);
        }
        Cancellation.Dispose();
    }
}
