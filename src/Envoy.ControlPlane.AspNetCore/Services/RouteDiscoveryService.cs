using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Route.V3.RouteDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class RouteDiscoveryService(SotwGrpcStream streams, DeltaGrpcStream delta, FetchGrpc fetch) : Contract.RouteDiscoveryServiceBase
{
    public override Task StreamRoutes(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Route);

    public override Task DeltaRoutes(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        delta.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Route);

    public override Task<DiscoveryResponse> FetchRoutes(DiscoveryRequest request, ServerCallContext context) =>
        fetch.RunAsync(request, context, ResourceTypeUrls.Route);
}
