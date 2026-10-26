using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Grpc.Net.Client;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal static class DiscoveryFetchCalls
{
    internal static AsyncUnaryCall<DiscoveryResponse> OpenFetch(GrpcChannel channel, string typeUrl, DiscoveryRequest request,
        CancellationToken cancellationToken, Metadata? headers = null) => typeUrl switch
    {
        ResourceTypeUrls.Cluster => new Envoy.Service.Cluster.V3.ClusterDiscoveryService.ClusterDiscoveryServiceClient(channel).FetchClustersAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Endpoint => new Envoy.Service.Endpoint.V3.EndpointDiscoveryService.EndpointDiscoveryServiceClient(channel).FetchEndpointsAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Listener => new Envoy.Service.Listener.V3.ListenerDiscoveryService.ListenerDiscoveryServiceClient(channel).FetchListenersAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Route => new Envoy.Service.Route.V3.RouteDiscoveryService.RouteDiscoveryServiceClient(channel).FetchRoutesAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.ScopedRoute => new Envoy.Service.Route.V3.ScopedRoutesDiscoveryService.ScopedRoutesDiscoveryServiceClient(channel).FetchScopedRoutesAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Secret => new Envoy.Service.Secret.V3.SecretDiscoveryService.SecretDiscoveryServiceClient(channel).FetchSecretsAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Runtime => new Envoy.Service.Runtime.V3.RuntimeDiscoveryService.RuntimeDiscoveryServiceClient(channel).FetchRuntimeAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.ExtensionConfig => new Envoy.Service.Extension.V3.ExtensionConfigDiscoveryService.ExtensionConfigDiscoveryServiceClient(channel).FetchExtensionConfigsAsync(request, headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.RateLimitConfig => new Ratelimit.Service.Ratelimit.V3.RateLimitConfigDiscoveryService.RateLimitConfigDiscoveryServiceClient(channel).FetchRlsConfigsAsync(request, headers, cancellationToken: cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(typeUrl)),
    };
}
