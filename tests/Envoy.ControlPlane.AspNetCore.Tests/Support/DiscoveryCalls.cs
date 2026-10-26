using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal static class DiscoveryCalls
{
    internal static AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse> Open(GrpcChannel channel, string typeUrl,
        CancellationToken cancellationToken, Grpc.Core.Metadata? headers = null) => typeUrl switch
    {
        "" => new AggregatedDiscoveryService.AggregatedDiscoveryServiceClient(channel).StreamAggregatedResources(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Cluster => new Envoy.Service.Cluster.V3.ClusterDiscoveryService.ClusterDiscoveryServiceClient(channel).StreamClusters(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Endpoint => new Envoy.Service.Endpoint.V3.EndpointDiscoveryService.EndpointDiscoveryServiceClient(channel).StreamEndpoints(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Listener => new Envoy.Service.Listener.V3.ListenerDiscoveryService.ListenerDiscoveryServiceClient(channel).StreamListeners(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Route => new Envoy.Service.Route.V3.RouteDiscoveryService.RouteDiscoveryServiceClient(channel).StreamRoutes(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.ScopedRoute => new Envoy.Service.Route.V3.ScopedRoutesDiscoveryService.ScopedRoutesDiscoveryServiceClient(channel).StreamScopedRoutes(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Secret => new Envoy.Service.Secret.V3.SecretDiscoveryService.SecretDiscoveryServiceClient(channel).StreamSecrets(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Runtime => new Envoy.Service.Runtime.V3.RuntimeDiscoveryService.RuntimeDiscoveryServiceClient(channel).StreamRuntime(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.ExtensionConfig => new Envoy.Service.Extension.V3.ExtensionConfigDiscoveryService.ExtensionConfigDiscoveryServiceClient(channel).StreamExtensionConfigs(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.RateLimitConfig => new Ratelimit.Service.Ratelimit.V3.RateLimitConfigDiscoveryService.RateLimitConfigDiscoveryServiceClient(channel).StreamRlsConfigs(headers, cancellationToken: cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(typeUrl)),
    };

    internal static DiscoveryRequest Request(string typeUrl = "", string version = "", string nonce = "", params string[] names)
    {
        var request = new DiscoveryRequest { Node = new Node { Id = "node" }, TypeUrl = typeUrl, VersionInfo = version, ResponseNonce = nonce };
        request.ResourceNames.Add(names);
        return request;
    }

    internal static Snapshot Snapshot(string typeUrl, string version, string name = "a") => new(version,
        new Dictionary<string, IEnumerable<IMessage?>> { [typeUrl] = [Message(typeUrl, name)] });

    internal static IMessage Message(string typeUrl, string name) => typeUrl switch
    {
        ResourceTypeUrls.Cluster => new Envoy.Config.Cluster.V3.Cluster { Name = name },
        ResourceTypeUrls.Endpoint => new Envoy.Config.Endpoint.V3.ClusterLoadAssignment { ClusterName = name },
        ResourceTypeUrls.Listener => new Envoy.Config.Listener.V3.Listener { Name = name },
        ResourceTypeUrls.Route => new Envoy.Config.Route.V3.RouteConfiguration { Name = name },
        ResourceTypeUrls.ScopedRoute => new Envoy.Config.Route.V3.ScopedRouteConfiguration { Name = name },
        ResourceTypeUrls.Secret => new Envoy.Extensions.TransportSockets.Tls.V3.Secret { Name = name },
        ResourceTypeUrls.Runtime => new Envoy.Service.Runtime.V3.Runtime { Name = name },
        ResourceTypeUrls.ExtensionConfig => new TypedExtensionConfig { Name = name },
        ResourceTypeUrls.RateLimitConfig => new Ratelimit.Config.Ratelimit.V3.RateLimitConfig { Name = name },
        _ => throw new ArgumentOutOfRangeException(nameof(typeUrl)),
    };
}
