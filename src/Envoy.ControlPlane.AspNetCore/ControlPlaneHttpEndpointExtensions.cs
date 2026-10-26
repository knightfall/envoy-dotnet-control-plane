using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Envoy.ControlPlane.AspNetCore;

/// <summary>Maps the optional protobuf JSON Fetch gateway.</summary>
public static class ControlPlaneHttpEndpointExtensions
{
    /// <summary>Maps eight cleaned xDS paths as a fallback; ordinary application endpoints take precedence.</summary>
    /// <param name="endpoints">The application's root endpoint builder.</param>
    /// <param name="typeRegistry">A complete Any registry; defaults to standard resource roots and TTL wrappers.</param>
    /// <returns>The endpoint conventions, including application authorization.</returns>
    /// <remarks>All HTTP methods reach the gateway. Host request limits and cancellation remain application policy.</remarks>
    public static IEndpointConventionBuilder MapEnvoyControlPlaneHttpGateway(this IEndpointRouteBuilder endpoints,
        TypeRegistry? typeRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var gateway = new HttpFetchGateway(typeRegistry ?? HttpFetchGateway.DefaultTypes);
        return endpoints.MapFallback("/{**path}", gateway.HandleAsync).WithDisplayName("Envoy xDS HTTP gateway");
    }
}
