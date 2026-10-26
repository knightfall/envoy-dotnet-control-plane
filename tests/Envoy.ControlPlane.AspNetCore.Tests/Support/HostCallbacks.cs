using System.Threading.Channels;
using System.Collections.Concurrent;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal sealed class HostCallbacks : ISotwCallbacks, IDeltaCallbacks
{
    internal Channel<ServerCallContext> Opened { get; } = Channel.CreateUnbounded<ServerCallContext>();
    internal Channel<DiscoveryRequest> Requested { get; } = Channel.CreateUnbounded<DiscoveryRequest>();
    internal Channel<DeltaDiscoveryRequest> DeltaRequested { get; } = Channel.CreateUnbounded<DeltaDiscoveryRequest>();
    internal Channel<DeltaDiscoveryResponse> DeltaResponses { get; } = Channel.CreateUnbounded<DeltaDiscoveryResponse>();
    internal Channel<Node> Closed { get; } = Channel.CreateUnbounded<Node>();
    internal Exception? RequestFailure { get; set; }
    internal Func<DiscoveryRequest, CancellationToken, ValueTask>? RequestHandler { get; set; }
    internal Func<DiscoveryResponse, CancellationToken, ValueTask>? ResponseHandler { get; set; }
    internal Func<DeltaDiscoveryRequest, CancellationToken, ValueTask>? DeltaRequestHandler { get; set; }
    internal ConcurrentQueue<long> StreamIds { get; } = new();

    public ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        StreamIds.Enqueue(streamId);
        Opened.Writer.TryWrite((ServerCallContext)context.State!);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamRequestAsync(long streamId, DiscoveryRequest request, CancellationToken cancellationToken)
    {
        Requested.Writer.TryWrite(request);
        return RequestFailure is null
            ? RequestHandler?.Invoke(request, cancellationToken) ?? ValueTask.CompletedTask
            : ValueTask.FromException(RequestFailure);
    }

    public ValueTask OnStreamResponseAsync(ResponseContext context, long streamId, DiscoveryRequest request,
        DiscoveryResponse response, CancellationToken cancellationToken) =>
        ResponseHandler?.Invoke(response, cancellationToken) ?? ValueTask.CompletedTask;

    public ValueTask OnStreamClosedAsync(long streamId, Node node)
    {
        Closed.Writer.TryWrite(node);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamRequestAsync(long streamId, DeltaDiscoveryRequest request, CancellationToken cancellationToken)
    {
        DeltaRequested.Writer.TryWrite(request);
        return RequestFailure is null
            ? DeltaRequestHandler?.Invoke(request, cancellationToken) ?? ValueTask.CompletedTask
            : ValueTask.FromException(RequestFailure);
    }

    public ValueTask OnStreamResponseAsync(long streamId, DeltaDiscoveryRequest request,
        DeltaDiscoveryResponse response, CancellationToken cancellationToken)
    {
        DeltaResponses.Writer.TryWrite(response);
        return ValueTask.CompletedTask;
    }
}
