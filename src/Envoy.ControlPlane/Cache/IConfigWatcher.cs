using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Creates thread-safe one-shot watches; creation errors are reported synchronously.</summary>
public interface IConfigWatcher
{
    /// <summary>Creates a SotW watch, which may already be complete when returned.</summary>
    /// <param name="request">The stable originating request.</param>
    /// <param name="subscription">The subscription view, stable until watch completion or disposal.</param>
    IWatch<ISotwResponse> CreateWatch(DiscoveryRequest request, ISubscription subscription);

    /// <summary>Creates a Delta watch, which may already be complete when returned.</summary>
    /// <param name="request">The stable originating Delta request.</param>
    /// <param name="subscription">The subscription view, stable until watch completion or disposal.</param>
    IWatch<IDeltaResponse> CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription);
}
