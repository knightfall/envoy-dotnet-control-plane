using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Listener.V3.ListenerDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class ListenerDiscoveryService(SotwGrpcStream streams, DeltaGrpcStream delta, FetchGrpc fetch) : Contract.ListenerDiscoveryServiceBase
{
    public override Task StreamListeners(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Listener);

    public override Task DeltaListeners(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        delta.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Listener);

    public override Task<DiscoveryResponse> FetchListeners(DiscoveryRequest request, ServerCallContext context) =>
        fetch.RunAsync(request, context, ResourceTypeUrls.Listener);
}
