namespace Envoy.ControlPlane.Resources;

/// <summary>Resource-family order used by the upstream cache and ordered ADS.</summary>
public enum ResponseType
{
    /// <summary>Cluster discovery.</summary>
    Cluster = 0,
    /// <summary>Endpoint discovery.</summary>
    Endpoint = 1,
    /// <summary>Individual load-balancing endpoints.</summary>
    LbEndpoint = 2,
    /// <summary>Listener discovery.</summary>
    Listener = 3,
    /// <summary>Route discovery.</summary>
    Route = 4,
    /// <summary>Scoped route discovery.</summary>
    ScopedRoute = 5,
    /// <summary>Virtual host discovery.</summary>
    VirtualHost = 6,
    /// <summary>Secret discovery.</summary>
    Secret = 7,
    /// <summary>Runtime discovery.</summary>
    Runtime = 8,
    /// <summary>Extension configuration discovery.</summary>
    ExtensionConfig = 9,
    /// <summary>Rate-limit configuration discovery.</summary>
    RateLimitConfig = 10,
    /// <summary>Filter-chain discovery.</summary>
    FilterChain = 11,
    /// <summary>Unknown type and count of supported families.</summary>
    Unknown = 12,
}
