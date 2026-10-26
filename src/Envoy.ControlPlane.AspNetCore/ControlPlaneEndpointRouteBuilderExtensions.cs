using Envoy.ControlPlane.AspNetCore.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Envoy.ControlPlane.AspNetCore;

/// <summary>Maps implemented SotW and Delta gRPC services using generated upstream contracts.</summary>
public static class ControlPlaneEndpointRouteBuilderExtensions
{
    /// <summary>Maps ADS, eight standard per-type services and Delta VHDS/LEDS, retaining endpoint conventions.</summary>
    /// <param name="endpoints">The application's endpoint builder.</param>
    /// <returns>The route group, for application authorization and other endpoint conventions.</returns>
    public static RouteGroupBuilder MapEnvoyControlPlane(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("");
        group.MapGrpcService<AggregatedDiscoveryService>();
        group.MapGrpcService<ClusterDiscoveryService>();
        group.MapGrpcService<EndpointDiscoveryService>();
        group.MapGrpcService<ListenerDiscoveryService>();
        group.MapGrpcService<RouteDiscoveryService>();
        group.MapGrpcService<ScopedRoutesDiscoveryService>();
        group.MapGrpcService<SecretDiscoveryService>();
        group.MapGrpcService<RuntimeDiscoveryService>();
        group.MapGrpcService<ExtensionConfigDiscoveryService>();
        group.MapGrpcService<VirtualHostDiscoveryService>();
        group.MapGrpcService<LocalityEndpointDiscoveryService>();
        return group;
    }
}
