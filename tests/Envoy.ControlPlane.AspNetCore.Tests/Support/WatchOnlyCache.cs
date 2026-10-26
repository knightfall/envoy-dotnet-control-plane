using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal sealed class WatchOnlyCache(IConfigWatcher inner) : IConfigWatcher
{
    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses) =>
        inner.CreateWatch(request, subscription, responses);

    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses) =>
        inner.CreateDeltaWatch(request, subscription, responses);
}
