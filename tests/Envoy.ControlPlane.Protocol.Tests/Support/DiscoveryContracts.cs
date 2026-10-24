using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Protocol.Tests;

internal static class DiscoveryContracts
{
    internal static readonly IReadOnlyList<DiscoveryContract> All =
    [
        new(
            typeof(Service.Cluster.V3.ClusterDiscoveryService),
            typeof(Service.Cluster.V3.ClusterDiscoveryService.ClusterDiscoveryServiceBase),
            typeof(Service.Cluster.V3.ClusterDiscoveryService.ClusterDiscoveryServiceClient),
            "envoy.service.cluster.v3.ClusterDiscoveryService",
            ["StreamClusters", "DeltaClusters", "FetchClusters"]),
        new(
            typeof(Service.Endpoint.V3.EndpointDiscoveryService),
            typeof(Service.Endpoint.V3.EndpointDiscoveryService.EndpointDiscoveryServiceBase),
            typeof(Service.Endpoint.V3.EndpointDiscoveryService.EndpointDiscoveryServiceClient),
            "envoy.service.endpoint.v3.EndpointDiscoveryService",
            ["StreamEndpoints", "DeltaEndpoints", "FetchEndpoints"]),
        new(
            typeof(Service.Listener.V3.ListenerDiscoveryService),
            typeof(Service.Listener.V3.ListenerDiscoveryService.ListenerDiscoveryServiceBase),
            typeof(Service.Listener.V3.ListenerDiscoveryService.ListenerDiscoveryServiceClient),
            "envoy.service.listener.v3.ListenerDiscoveryService",
            ["StreamListeners", "DeltaListeners", "FetchListeners"]),
        new(
            typeof(Service.Route.V3.RouteDiscoveryService),
            typeof(Service.Route.V3.RouteDiscoveryService.RouteDiscoveryServiceBase),
            typeof(Service.Route.V3.RouteDiscoveryService.RouteDiscoveryServiceClient),
            "envoy.service.route.v3.RouteDiscoveryService",
            ["StreamRoutes", "DeltaRoutes", "FetchRoutes"]),
        new(
            typeof(Service.Route.V3.ScopedRoutesDiscoveryService),
            typeof(Service.Route.V3.ScopedRoutesDiscoveryService.ScopedRoutesDiscoveryServiceBase),
            typeof(Service.Route.V3.ScopedRoutesDiscoveryService.ScopedRoutesDiscoveryServiceClient),
            "envoy.service.route.v3.ScopedRoutesDiscoveryService",
            ["StreamScopedRoutes", "DeltaScopedRoutes", "FetchScopedRoutes"]),
        new(
            typeof(Service.Runtime.V3.RuntimeDiscoveryService),
            typeof(Service.Runtime.V3.RuntimeDiscoveryService.RuntimeDiscoveryServiceBase),
            typeof(Service.Runtime.V3.RuntimeDiscoveryService.RuntimeDiscoveryServiceClient),
            "envoy.service.runtime.v3.RuntimeDiscoveryService",
            ["StreamRuntime", "DeltaRuntime", "FetchRuntime"]),
        new(
            typeof(Service.Secret.V3.SecretDiscoveryService),
            typeof(Service.Secret.V3.SecretDiscoveryService.SecretDiscoveryServiceBase),
            typeof(Service.Secret.V3.SecretDiscoveryService.SecretDiscoveryServiceClient),
            "envoy.service.secret.v3.SecretDiscoveryService",
            ["StreamSecrets", "DeltaSecrets", "FetchSecrets"]),
        new(
            typeof(Service.Extension.V3.ExtensionConfigDiscoveryService),
            typeof(Service.Extension.V3.ExtensionConfigDiscoveryService.ExtensionConfigDiscoveryServiceBase),
            typeof(Service.Extension.V3.ExtensionConfigDiscoveryService.ExtensionConfigDiscoveryServiceClient),
            "envoy.service.extension.v3.ExtensionConfigDiscoveryService",
            ["StreamExtensionConfigs", "DeltaExtensionConfigs", "FetchExtensionConfigs"]),
        new(
            typeof(AggregatedDiscoveryService),
            typeof(AggregatedDiscoveryService.AggregatedDiscoveryServiceBase),
            typeof(AggregatedDiscoveryService.AggregatedDiscoveryServiceClient),
            "envoy.service.discovery.v3.AggregatedDiscoveryService",
            ["StreamAggregatedResources", "DeltaAggregatedResources"]),
        new(
            typeof(Ratelimit.Service.Ratelimit.V3.RateLimitConfigDiscoveryService),
            typeof(Ratelimit.Service.Ratelimit.V3.RateLimitConfigDiscoveryService.RateLimitConfigDiscoveryServiceBase),
            typeof(Ratelimit.Service.Ratelimit.V3.RateLimitConfigDiscoveryService.RateLimitConfigDiscoveryServiceClient),
            "ratelimit.service.ratelimit.v3.RateLimitConfigDiscoveryService",
            ["StreamRlsConfigs", "FetchRlsConfigs"]),
    ];
}
