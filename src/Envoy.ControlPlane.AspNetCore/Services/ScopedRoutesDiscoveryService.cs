using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Route.V3.ScopedRoutesDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class ScopedRoutesDiscoveryService(SotwGrpcStream streams, DeltaGrpcStream delta) : Contract.ScopedRoutesDiscoveryServiceBase
{
    public override Task StreamScopedRoutes(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.ScopedRoute);

    public override Task DeltaScopedRoutes(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        delta.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.ScopedRoute);
}
