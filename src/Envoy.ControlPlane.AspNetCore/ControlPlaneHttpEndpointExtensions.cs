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
    /// <remarks>All HTTP methods reach the gateway. The default request body limit is 1 MiB.</remarks>
    public static IEndpointConventionBuilder MapEnvoyControlPlaneHttpGateway(this IEndpointRouteBuilder endpoints,
        TypeRegistry? typeRegistry = null) => endpoints.MapEnvoyControlPlaneHttpGateway(1024 * 1024, typeRegistry);

    /// <summary>Maps the gateway with an application-configured request body size limit.</summary>
    /// <param name="endpoints">The application's root endpoint builder.</param>
    /// <param name="maxRequestBodyBytes">The positive maximum size of an accepted request body, in bytes.</param>
    /// <param name="typeRegistry">A complete Any registry; defaults to standard resource roots and TTL wrappers.</param>
    /// <returns>The endpoint conventions, including application authorization.</returns>
    public static IEndpointConventionBuilder MapEnvoyControlPlaneHttpGateway(this IEndpointRouteBuilder endpoints,
        int maxRequestBodyBytes, TypeRegistry? typeRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequestBodyBytes);
        var gateway = new HttpFetchGateway(typeRegistry ?? HttpFetchGateway.DefaultTypes, maxRequestBodyBytes);
        return endpoints.MapFallback("/{**path}", gateway.HandleAsync).WithDisplayName("Envoy xDS HTTP gateway");
    }
}
