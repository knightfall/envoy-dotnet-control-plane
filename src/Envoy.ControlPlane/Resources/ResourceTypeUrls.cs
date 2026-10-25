namespace Envoy.ControlPlane.Resources;

/// <summary>Upstream v3 resource type URLs.</summary>
public static class ResourceTypeUrls
{
    /// <summary>Canonical protobuf type URL prefix.</summary>
    public const string Prefix = "type.googleapis.com/";
    /// <summary>Endpoint assignment type.</summary>
    public const string Endpoint = Prefix + "envoy.config.endpoint.v3.ClusterLoadAssignment";
    /// <summary>Cluster type.</summary>
    public const string Cluster = Prefix + "envoy.config.cluster.v3.Cluster";
    /// <summary>Route configuration type.</summary>
    public const string Route = Prefix + "envoy.config.route.v3.RouteConfiguration";
    /// <summary>Scoped route configuration type.</summary>
    public const string ScopedRoute = Prefix + "envoy.config.route.v3.ScopedRouteConfiguration";
    /// <summary>Virtual host type.</summary>
    public const string VirtualHost = Prefix + "envoy.config.route.v3.VirtualHost";
    /// <summary>Listener type.</summary>
    public const string Listener = Prefix + "envoy.config.listener.v3.Listener";
    /// <summary>Filter-chain type.</summary>
    public const string FilterChain = Prefix + "envoy.config.listener.v3.FilterChain";
    /// <summary>TLS secret type.</summary>
    public const string Secret = Prefix + "envoy.extensions.transport_sockets.tls.v3.Secret";
    /// <summary>Extension configuration type.</summary>
    public const string ExtensionConfig = Prefix + "envoy.config.core.v3.TypedExtensionConfig";
    /// <summary>Runtime type.</summary>
    public const string Runtime = Prefix + "envoy.service.runtime.v3.Runtime";
    /// <summary>Thrift route type; not a default snapshot response family.</summary>
    public const string ThriftRoute = Prefix + "envoy.extensions.filters.network.thrift_proxy.v3.RouteConfiguration";
    /// <summary>Rate-limit configuration type.</summary>
    public const string RateLimitConfig = Prefix + "ratelimit.config.ratelimit.v3.RateLimitConfig";
    /// <summary>Individual load-balancing endpoint type.</summary>
    public const string LbEndpoint = Prefix + "envoy.config.endpoint.v3.LbEndpoint";
    /// <summary>Empty type URL used by aggregated discovery.</summary>
    public const string Any = "";
}
