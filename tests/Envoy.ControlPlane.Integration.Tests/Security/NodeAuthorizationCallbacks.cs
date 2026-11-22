using System.Collections.Concurrent;
using System.Security.Claims;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;

namespace Envoy.ControlPlane.Integration.Tests.Security;

internal sealed class NodeAuthorizationCallbacks(ControlPlaneObservations observations) : ISotwCallbacks, IDeltaCallbacks
{
    internal const string NodeClaim = "xds-node";
    private readonly ConcurrentDictionary<long, NodeBinding> sotw = new();
    private readonly ConcurrentDictionary<long, NodeBinding> delta = new();
    internal int ActiveBindings => sotw.Count + delta.Count;

    ValueTask ISotwCallbacks.OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        sotw[streamId] = Bind(context);
        return ((ISotwCallbacks)observations).OnStreamOpenAsync(context, streamId, typeUrl, cancellationToken);
    }

    ValueTask IDeltaCallbacks.OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        delta[streamId] = Bind(context);
        return ((IDeltaCallbacks)observations).OnStreamOpenAsync(context, streamId, typeUrl, cancellationToken);
    }

    public async ValueTask OnStreamRequestAsync(long streamId, DiscoveryRequest request, CancellationToken cancellationToken)
    {
        await observations.OnStreamRequestAsync(streamId, request, cancellationToken);
        Authorize(sotw[streamId], request.Node);
    }

    public async ValueTask OnStreamRequestAsync(long streamId, DeltaDiscoveryRequest request, CancellationToken cancellationToken)
    {
        await observations.OnStreamRequestAsync(streamId, request, cancellationToken);
        Authorize(delta[streamId], request.Node);
    }

    public ValueTask OnStreamResponseAsync(ResponseContext context, long streamId, DiscoveryRequest request,
        DiscoveryResponse response, CancellationToken cancellationToken) =>
        observations.OnStreamResponseAsync(context, streamId, request, response, cancellationToken);

    public ValueTask OnStreamResponseAsync(long streamId, DeltaDiscoveryRequest request,
        DeltaDiscoveryResponse response, CancellationToken cancellationToken) =>
        observations.OnStreamResponseAsync(streamId, request, response, cancellationToken);

    ValueTask ISotwCallbacks.OnStreamClosedAsync(long streamId, Node node)
    {
        sotw.TryRemove(streamId, out _);
        return ((ISotwCallbacks)observations).OnStreamClosedAsync(streamId, node);
    }

    ValueTask IDeltaCallbacks.OnStreamClosedAsync(long streamId, Node node)
    {
        delta.TryRemove(streamId, out _);
        return ((IDeltaCallbacks)observations).OnStreamClosedAsync(streamId, node);
    }

    private static NodeBinding Bind(ResponseContext context)
    {
        var user = ((ServerCallContext)context.State!).GetHttpContext().User;
        string? nodeId = user.FindFirstValue(NodeClaim);
        return nodeId is not null ? new NodeBinding(nodeId) : throw Denied();
    }

    private static void Authorize(NodeBinding binding, Node? requestedNode)
    {
        if (requestedNode is null && binding.Verified)
        {
            return;
        }
        if (requestedNode?.Id != binding.NodeId)
        {
            throw Denied();
        }
        binding.Verified = true;
    }

    private static RpcException Denied() => new(new Status(StatusCode.PermissionDenied, "The bearer identity does not match the requested node."));

    private sealed class NodeBinding(string nodeId)
    {
        internal string NodeId { get; } = nodeId;
        internal bool Verified { get; set; }
    }
}
