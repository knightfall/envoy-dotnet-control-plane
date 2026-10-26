using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Secret.V3.SecretDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class SecretDiscoveryService(SotwGrpcStream streams, DeltaGrpcStream delta, FetchGrpc fetch) : Contract.SecretDiscoveryServiceBase
{
    public override Task StreamSecrets(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Secret);

    public override Task DeltaSecrets(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        delta.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Secret);

    public override Task<DiscoveryResponse> FetchSecrets(DiscoveryRequest request, ServerCallContext context) =>
        fetch.RunAsync(request, context, ResourceTypeUrls.Secret);
}
