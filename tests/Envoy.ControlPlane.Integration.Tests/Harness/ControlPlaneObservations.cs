using System.Collections.Concurrent;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal sealed class ControlPlaneObservations : ISotwCallbacks, IDeltaCallbacks, IFetchCallbacks
{
    internal StreamObservations<DiscoveryRequest, DiscoveryResponse> Sotw { get; } = new();
    internal StreamObservations<DeltaDiscoveryRequest, DeltaDiscoveryResponse> Delta { get; } = new();
    internal ConcurrentQueue<DiscoveryRequest> FetchRequests { get; } = new();
    internal ConcurrentQueue<(DiscoveryRequest Request, DiscoveryResponse? Response)> FetchResponses { get; } = new();

    ValueTask ISotwCallbacks.OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        Sotw.Opened.Enqueue((streamId, typeUrl));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamRequestAsync(long streamId, DiscoveryRequest request, CancellationToken cancellationToken)
    {
        Sotw.Requests.Enqueue((streamId, request.Clone()));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamResponseAsync(ResponseContext context, long streamId, DiscoveryRequest request,
        DiscoveryResponse response, CancellationToken cancellationToken)
    {
        Sotw.Responses.Enqueue((streamId, response.Clone()));
        return ValueTask.CompletedTask;
    }

    ValueTask ISotwCallbacks.OnStreamClosedAsync(long streamId, Node node)
    {
        Sotw.Closed.Enqueue((streamId, node.Clone()));
        return ValueTask.CompletedTask;
    }

    ValueTask IDeltaCallbacks.OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        Delta.Opened.Enqueue((streamId, typeUrl));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamRequestAsync(long streamId, DeltaDiscoveryRequest request, CancellationToken cancellationToken)
    {
        Delta.Requests.Enqueue((streamId, request.Clone()));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamResponseAsync(long streamId, DeltaDiscoveryRequest request,
        DeltaDiscoveryResponse response, CancellationToken cancellationToken)
    {
        Delta.Responses.Enqueue((streamId, response.Clone()));
        return ValueTask.CompletedTask;
    }

    ValueTask IDeltaCallbacks.OnStreamClosedAsync(long streamId, Node node)
    {
        Delta.Closed.Enqueue((streamId, node.Clone()));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFetchRequestAsync(ResponseContext context, DiscoveryRequest request)
    {
        FetchRequests.Enqueue(request.Clone());
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFetchResponseAsync(DiscoveryRequest request, DiscoveryResponse? response)
    {
        FetchResponses.Enqueue((request.Clone(), response?.Clone()));
        return ValueTask.CompletedTask;
    }
}

internal sealed class StreamObservations<TRequest, TResponse>
{
    internal ConcurrentQueue<(long StreamId, string TypeUrl)> Opened { get; } = new();
    internal ConcurrentQueue<(long StreamId, TRequest Request)> Requests { get; } = new();
    internal ConcurrentQueue<(long StreamId, TResponse Response)> Responses { get; } = new();
    internal ConcurrentQueue<(long StreamId, Node Node)> Closed { get; } = new();
}
