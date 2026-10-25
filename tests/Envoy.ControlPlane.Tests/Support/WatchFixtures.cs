using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Tests.Support;

internal sealed record TestSubscription(IReadOnlyDictionary<string, string> ReturnedResources,
    IReadOnlySet<string> SubscribedResources, IReadOnlySet<string> SubscribedPrefixes, bool IsWildcard) : ISubscription;

internal sealed class TestSotwResponse(DiscoveryRequest request, string version, IReadOnlyDictionary<string, string> returned,
    ResponseContext context, Func<DiscoveryResponse> materialize) : ISotwResponse
{
    public DiscoveryRequest Request => request;
    public string ResponseVersion => version;
    public IReadOnlyDictionary<string, string> ReturnedResources => returned;
    public ResponseContext Context => context;
    public int Materializations { get; private set; }
    public DiscoveryResponse GetDiscoveryResponse()
    {
        Materializations++;
        return materialize();
    }
}

internal sealed class TestDeltaResponse(DeltaDiscoveryRequest request, string version, IReadOnlyDictionary<string, string> returned,
    ResponseContext context, DeltaDiscoveryResponse response) : IDeltaResponse
{
    public DeltaDiscoveryRequest Request => request;
    public string ResponseVersion => version;
    public IReadOnlyDictionary<string, string> ReturnedResources => returned;
    public ResponseContext Context => context;
    public DeltaDiscoveryResponse GetDeltaDiscoveryResponse() => response;
}

internal sealed class ImmediateWatcher(ISotwResponse sotw, IDeltaResponse delta) : IConfigWatcher
{
    public ISubscription? LastSubscription { get; private set; }
    public object? LastRequest { get; private set; }
    public IWatch<ISotwResponse> CreateWatch(DiscoveryRequest request, ISubscription subscription)
    {
        LastRequest = request;
        LastSubscription = subscription;
        var watch = new Watch<ISotwResponse>();
        watch.TrySetResult(sotw);
        return watch;
    }

    public IWatch<IDeltaResponse> CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription)
    {
        LastRequest = request;
        LastSubscription = subscription;
        var watch = new Watch<IDeltaResponse>();
        watch.TrySetResult(delta);
        return watch;
    }
}

internal sealed class RejectingWatcher(Exception failure) : IConfigWatcher
{
    public IWatch<ISotwResponse> CreateWatch(DiscoveryRequest request, ISubscription subscription) => throw failure;
    public IWatch<IDeltaResponse> CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription) => throw failure;
}
