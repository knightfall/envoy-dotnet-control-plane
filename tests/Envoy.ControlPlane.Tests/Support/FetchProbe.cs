using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Tests.Support;

internal sealed class FetchProbe : IConfigFetcher, IFetchCallbacks
{
    internal List<string> Events { get; } = [];
    internal Func<DiscoveryRequest, ISotwResponse> Produce { get; set; } = _ => throw new InvalidOperationException("Unconfigured producer");
    internal Action<DiscoveryRequest>? BeforeRequest { get; set; }
    internal Action<DiscoveryResponse?>? BeforeResponse { get; set; }
    internal DiscoveryRequest? LastRequest { get; private set; }
    internal DiscoveryRequest? ResponseRequest { get; private set; }
    internal ResponseContext LastContext { get; private set; }
    internal ResponseContext CallbackContext { get; private set; }

    public Task<ISotwResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default)
    {
        Events.Add("fetch");
        LastRequest = request;
        LastContext = context;
        return Task.FromResult(Produce(request));
    }

    public ValueTask OnFetchRequestAsync(ResponseContext context, DiscoveryRequest request)
    {
        Events.Add("request");
        CallbackContext = context;
        BeforeRequest?.Invoke(request);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFetchResponseAsync(DiscoveryRequest request, DiscoveryResponse? response)
    {
        ResponseRequest = request;
        Events.Add(response is null ? "response:null" : "response");
        BeforeResponse?.Invoke(response);
        return ValueTask.CompletedTask;
    }
}
