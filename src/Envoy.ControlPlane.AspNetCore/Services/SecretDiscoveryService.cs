using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Secret.V3.SecretDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class SecretDiscoveryService(SotwGrpcStream streams) : Contract.SecretDiscoveryServiceBase
{
    public override Task StreamSecrets(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.Secret);
}
