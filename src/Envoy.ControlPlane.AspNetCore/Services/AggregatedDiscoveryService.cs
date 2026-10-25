using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Discovery.V3.AggregatedDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class AggregatedDiscoveryService(SotwGrpcStream streams) : Contract.AggregatedDiscoveryServiceBase
{
    public override Task StreamAggregatedResources(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, "");
}
