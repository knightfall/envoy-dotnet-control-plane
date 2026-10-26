using System.Threading.Channels;
using Envoy.Service.Discovery.V3;
using Grpc.Core;

namespace Envoy.ControlPlane.Tests.Support;

internal sealed class ScriptedAdsInvoker : CallInvoker
{
    internal List<ScriptedAdsCall> Calls { get; } = [];
    internal string? Method { get; private set; }
    internal CallOptions Options { get; private set; }
    internal Exception? OpenFailure { get; set; }
    internal ScriptedAdsCall? NextCall { get; set; }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        Method = method.FullName;
        Options = options;
        if (OpenFailure is { } failure)
        {
            OpenFailure = null;
            throw failure;
        }
        var session = NextCall ?? new ScriptedAdsCall();
        NextCall = null;
        Calls.Add(session);
        return new AsyncDuplexStreamingCall<TRequest, TResponse>((IClientStreamWriter<TRequest>)(object)session,
            (IAsyncStreamReader<TResponse>)(object)session, Task.FromResult(new Metadata()), () => Status.DefaultSuccess,
            () => new Metadata(), session.Dispose);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
}

internal sealed class ScriptedAdsCall : IClientStreamWriter<DiscoveryRequest>, IAsyncStreamReader<DiscoveryResponse>, IDisposable
{
    private readonly Channel<AdsRead> incoming = Channel.CreateUnbounded<AdsRead>();
    private readonly CancellationTokenSource stopped = new();
    internal Channel<int> Reading { get; } = Channel.CreateUnbounded<int>();
    internal List<DiscoveryRequest> Requests { get; } = [];
    internal Exception? SendFailure { get; set; }
    internal Func<CancellationToken, Task>? BeforeSend { get; set; }
    internal int Disposals { get; private set; }
    internal int Reads { get; private set; }
    public DiscoveryResponse Current { get; private set; } = null!;
    public WriteOptions? WriteOptions { get; set; }

    internal void Enqueue(DiscoveryResponse? response = null, Exception? failure = null, bool eof = false)
    {
        if (!incoming.Writer.TryWrite(new AdsRead(response, failure, eof)))
        {
            throw new InvalidOperationException("Fixture input closed unexpectedly");
        }
    }

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopped.Token);
        Reads++;
        Reading.Writer.TryWrite(Reads);
        var next = await incoming.Reader.ReadAsync(stop.Token);
        if (next.Failure is { } failure)
        {
            throw failure;
        }
        if (next.Eof)
        {
            return false;
        }
        Current = next.Response!;
        return true;
    }

    public Task WriteAsync(DiscoveryRequest message) => WriteAsync(message, default);

    public async Task WriteAsync(DiscoveryRequest message, CancellationToken cancellationToken)
    {
        Requests.Add(message.Clone());
        if (SendFailure is { } failure)
        {
            SendFailure = null;
            throw failure;
        }
        if (BeforeSend is not null)
        {
            await BeforeSend(cancellationToken);
        }
    }

    public Task CompleteAsync() => Task.CompletedTask;

    public void Dispose()
    {
        Disposals++;
        stopped.Cancel();
        stopped.Dispose();
    }
}

internal sealed record AdsRead(DiscoveryResponse? Response, Exception? Failure, bool Eof);
