using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>A custom or lazily materialized state-of-the-world cache response.</summary>
public interface ISotwResponse : IWatchResponse
{
    /// <summary>Gets the originating request for response correlation.</summary>
    DiscoveryRequest Request { get; }

    /// <summary>Materializes the discovery response; producer failures propagate to the caller.</summary>
    DiscoveryResponse GetDiscoveryResponse();
}
