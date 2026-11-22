using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.Service.Discovery.V3;
using Xunit;

namespace Envoy.ControlPlane.Integration.Tests.Delta;

internal static class DeltaAssertions
{
    internal const string RequestPath = "/delta";
    internal static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(1);

    internal static Task WaitForAcksAsync(LiveEnvoyScenario scenario, string version, params string[] types) =>
        scenario.UntilAsync(() => types.All(type => Responses(scenario).Any(sent =>
            sent.Response.TypeUrl == type && sent.Response.SystemVersionInfo == version && Acked(scenario, sent))),
            $"Delta ACKs for {version}: {string.Join(", ", types)}");

    internal static IEnumerable<(long StreamId, DeltaDiscoveryResponse Response)> Responses(LiveEnvoyScenario scenario) =>
        scenario.Host.Observations.Delta.Responses;

    internal static bool Acked(LiveEnvoyScenario scenario, (long StreamId, DeltaDiscoveryResponse Response) sent, int requestStart = 0) =>
        sent.Response.Nonce.Length > 0 && scenario.Host.Observations.Delta.Requests.Skip(requestStart).Any(received =>
            received.StreamId == sent.StreamId && received.Request.TypeUrl == sent.Response.TypeUrl
            && received.Request.ResponseNonce == sent.Response.Nonce && received.Request.ErrorDetail is null);

    internal static async Task AssertQuietAsync(LiveEnvoyScenario scenario, int responseCount)
    {
        await Task.Delay(QuietWindow, scenario.Token);
        Assert.Empty(Responses(scenario).Skip(responseCount));
    }

    internal static async Task StopAndVerifyAsync(LiveEnvoyScenario scenario, EnvoyProxyContainer proxy)
    {
        await proxy.StopAsync(scenario.Token);
        var wire = scenario.Host.Observations.Delta;
        await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumDeltaWatches: 0 }
            && wire.Closed.Count == wire.Opened.Count, "all Delta streams and cache watches to close");
        Assert.NotEmpty(wire.Opened);
        Assert.Equal(wire.Opened.Select(opened => opened.StreamId).Order(), wire.Closed.Select(closed => closed.StreamId).Order());
        Assert.All(wire.Closed, closed => Assert.Equal(scenario.NodeId, closed.Node.Id));
        Assert.All(wire.Requests, received => Assert.Null(received.Request.ErrorDetail));
    }

    internal static void AssertRemoval(DeltaDiscoveryResponse response, string name)
    {
        Assert.Empty(response.Resources);
        Assert.Equal([name], response.RemovedResources);
    }
}
