using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Cluster.V3.ClusterDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class ClusterDiscoveryService(SotwGrpcStream streams) : Contract.ClusterDiscoveryServiceBase
{
    public override Task StreamClusters(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Cluster);
}
