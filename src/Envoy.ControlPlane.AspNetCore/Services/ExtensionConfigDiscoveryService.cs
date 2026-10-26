using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Contract = Envoy.Service.Extension.V3.ExtensionConfigDiscoveryService;

namespace Envoy.ControlPlane.AspNetCore.Services;

internal sealed class ExtensionConfigDiscoveryService(SotwGrpcStream streams, DeltaGrpcStream delta) : Contract.ExtensionConfigDiscoveryServiceBase
{
    public override Task StreamExtensionConfigs(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context) =>
        streams.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.ExtensionConfig);

    public override Task DeltaExtensionConfigs(IAsyncStreamReader<DeltaDiscoveryRequest> requestStream,
        IServerStreamWriter<DeltaDiscoveryResponse> responseStream, ServerCallContext context) =>
        delta.RunAsync(requestStream, responseStream, context, ResourceTypeUrls.ExtensionConfig);
}
