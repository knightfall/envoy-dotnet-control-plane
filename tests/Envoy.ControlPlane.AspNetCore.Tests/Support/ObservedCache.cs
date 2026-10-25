using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal sealed class ObservedCache : IConfigWatcher, IDisposable
{
    internal SnapshotCache Inner { get; } = new();
    internal Channel<DiscoveryRequest> Created { get; } = Channel.CreateUnbounded<DiscoveryRequest>();
    internal bool CloseResponsesOnCreate { get; set; }
    internal int Disposals { get; private set; }

    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses)
    {
        var registration = Inner.CreateWatch(request, subscription, responses);
        if (CloseResponsesOnCreate)
        {
            responses.TryComplete();
        }
        Created.Writer.TryWrite(request);
        return registration;
    }

    public IWatch<IDeltaResponse> CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription) =>
        Inner.CreateDeltaWatch(request, subscription);

    public void Dispose() => Disposals++;
}
