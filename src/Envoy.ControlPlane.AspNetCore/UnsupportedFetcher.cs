using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class UnsupportedFetcher : IConfigFetcher
{
    public Task<ISotwResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default) =>
        throw new NotSupportedException("The registered watcher does not support Fetch. Register IConfigFetcher to enable polling.");
}
