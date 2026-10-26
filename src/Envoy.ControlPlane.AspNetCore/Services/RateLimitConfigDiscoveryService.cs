using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Ratelimit.Service.Ratelimit.V3.RateLimitConfigDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class RateLimitConfigDiscoveryService(SotwGrpcStream streams, FetchGrpc fetch)
    : Contract.RateLimitConfigDiscoveryServiceBase
{
    public override Task StreamRlsConfigs(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.RateLimitConfig);

    public override Task<DiscoveryResponse> FetchRlsConfigs(DiscoveryRequest request, ServerCallContext context) =>
        fetch.RunAsync(request, context, ResourceTypeUrls.RateLimitConfig);
}
