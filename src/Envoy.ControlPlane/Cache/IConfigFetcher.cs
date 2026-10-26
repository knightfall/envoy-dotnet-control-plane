using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Provides optional polling support independently of streaming watches.</summary>
public interface IConfigFetcher
{
    /// <summary>Fetches one response, preserving the originating request and caller context.</summary>
    /// <param name="request">The stable originating request.</param>
    /// <param name="context">Request cancellation and optional state for response correlation.</param>
    Task<ISotwResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default);
}
