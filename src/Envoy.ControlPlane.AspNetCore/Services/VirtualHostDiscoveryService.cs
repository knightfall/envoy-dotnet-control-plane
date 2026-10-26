using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Route.V3.VirtualHostDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class VirtualHostDiscoveryService(DeltaGrpcStream streams) : Contract.VirtualHostDiscoveryServiceBase
{
    public override Task DeltaVirtualHosts(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.VirtualHost);
}
