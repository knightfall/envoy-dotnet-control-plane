using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Runtime.V3.RuntimeDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class RuntimeDiscoveryService(SotwGrpcStream streams, DeltaGrpcStream delta) : Contract.RuntimeDiscoveryServiceBase
{
    public override Task StreamRuntime(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Runtime);

    public override Task DeltaRuntime(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        delta.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Runtime);
}
