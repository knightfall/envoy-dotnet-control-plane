using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Listener.V3.ListenerDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class ListenerDiscoveryService(SotwGrpcStream streams) : Contract.ListenerDiscoveryServiceBase
{
    public override Task StreamListeners(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Listener);
}
