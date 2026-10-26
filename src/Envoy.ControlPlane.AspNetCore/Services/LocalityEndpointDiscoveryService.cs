using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Endpoint.V3.LocalityEndpointDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class LocalityEndpointDiscoveryService(DeltaGrpcStream streams) : Contract.LocalityEndpointDiscoveryServiceBase
{
    public override Task DeltaLocalityEndpoints(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.LbEndpoint);
}
