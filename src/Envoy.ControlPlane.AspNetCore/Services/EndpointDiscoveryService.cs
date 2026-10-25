using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Endpoint.V3.EndpointDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class EndpointDiscoveryService(SotwGrpcStream streams) : Contract.EndpointDiscoveryServiceBase
{
    public override Task StreamEndpoints(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Endpoint);
}
