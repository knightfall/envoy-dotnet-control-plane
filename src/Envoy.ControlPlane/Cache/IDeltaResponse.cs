using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>A custom or lazily materialized incremental cache response.</summary>
public interface IDeltaResponse : IWatchResponse
{
    /// <summary>Gets the originating Delta request for response correlation.</summary>
    DeltaDiscoveryRequest Request { get; }

    /// <summary>Materializes the Delta response; producer failures propagate to the caller.</summary>
    DeltaDiscoveryResponse GetDeltaDiscoveryResponse();
}
