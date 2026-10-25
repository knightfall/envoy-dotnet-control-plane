using System.Threading.Channels;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Creates thread-safe one-shot watches; creation errors are reported synchronously.</summary>
public interface IConfigWatcher
{
    /// <summary>Registers one SotW response with a caller-owned asynchronous destination.</summary>
    /// <param name="request">The stable originating request.</param>
    /// <param name="subscription">The subscription view, stable until watch completion or disposal.</param>
    /// <param name="responses">The destination; releasing one watch must not complete a shared writer.</param>
    /// <returns>A cancellation registration; implement IAsyncDisposable when delivery owns asynchronous work.</returns>
    /// <remarks>Assign delivery order by writing to the destination. DisposeAsync must settle owned writes before subscription mutation.</remarks>
    IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses);

    /// <summary>Creates a Delta watch, which may already be complete when returned.</summary>
    /// <param name="request">The stable originating Delta request.</param>
    /// <param name="subscription">The subscription view, stable until watch completion or disposal.</param>
    IWatch<IDeltaResponse> CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription);
}
