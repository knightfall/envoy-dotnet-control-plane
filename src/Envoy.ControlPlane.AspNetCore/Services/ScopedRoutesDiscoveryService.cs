using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Route.V3.ScopedRoutesDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class ScopedRoutesDiscoveryService(SotwGrpcStream streams) : Contract.ScopedRoutesDiscoveryServiceBase
{
    public override Task StreamScopedRoutes(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.ScopedRoute);
}
