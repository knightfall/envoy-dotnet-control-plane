using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Fleet;

public sealed class FleetDiscoveryTests(ITestOutputHelper output)
{
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private const string FleetName = "edge-fleet";
    private const string RequestPath = "/fleet";

    [Fact]
    public Task Ads_IsolatesEachNodeAndKeepsItsPeerLiveAfterDisconnect() => RunAsync(async scenario =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
        var initialA = Single("node-a-initial", backendA.Address);
        var initialB = Single("node-b-initial", backendB.Address);
        await scenario.PublishAsync(initialA, NodeA);
        await scenario.PublishAsync(initialB, NodeB);
        await using var proxyA = CreateProxy(scenario, NodeA);
        await using var proxyB = CreateProxy(scenario, NodeB);

        await proxyA.RunWithDiagnosticsAsync(output, async () =>
        {
            await Task.WhenAll(
                scenario.ExpectTrafficAsync(proxyA, HttpPort, "A:/fleet", path: RequestPath),
                scenario.ExpectTrafficAsync(proxyB, HttpPort, "B:/fleet", path: RequestPath));
            long streamA = StreamForNode(scenario, NodeA);
            long streamB = StreamForNode(scenario, NodeB);
            Assert.NotEqual(streamA, streamB);
            Assert.Equal(2, scenario.Observations.Opened.Count);
            await WaitForGraphAsync(scenario, initialA, streamA);
            await WaitForGraphAsync(scenario, initialB, streamB);
            await scenario.UntilAsync(() => Watches(scenario, NodeA) == 4 && Watches(scenario, NodeB) == 4,
                "four active watches for each isolated proxy");

            int beforeUpdate = scenario.Observations.Responses.Count;
            var routeAtoB = Assignment(ServiceA, backendB.Address);
            await scenario.PublishAsync(Replace(initialA, ResourceTypeUrls.Endpoint, "node-a-shifted", routeAtoB), NodeA);
            await AssertEndpointUpdateAsync(scenario, streamA, "node-a-shifted", routeAtoB);
            await Task.WhenAll(
                scenario.ExpectTrafficAsync(proxyA, HttpPort, "B:/fleet", path: RequestPath),
                scenario.ExpectTrafficAsync(proxyB, HttpPort, "B:/fleet", path: RequestPath));
            await Task.Delay(TimeSpan.FromSeconds(1), scenario.Token);
            Assert.Equal(streamA, Assert.Single(scenario.Observations.Responses.Skip(beforeUpdate)).StreamId);
            Assert.Same(initialB, scenario.Cache.GetSnapshot(NodeB));

            await proxyA.StopAsync(scenario.Token);
            await scenario.UntilAsync(() => Watches(scenario, NodeA) == 0 && scenario.Observations.Closed.Count == 1,
                "only node-a's ADS stream and four watches to close");
            var departureA = Assert.Single(scenario.Observations.Closed);
            Assert.Equal((streamA, NodeA), (departureA.StreamId, departureA.Node.Id));
            Assert.Equal(4, Watches(scenario, NodeB));
            await scenario.ExpectTrafficAsync(proxyB, HttpPort, "B:/fleet", path: RequestPath);

            int beforePeerUpdate = scenario.Observations.Responses.Count;
            var routeBtoA = Assignment(ServiceA, backendA.Address);
            await scenario.PublishAsync(Replace(initialB, ResourceTypeUrls.Endpoint, "node-b-shifted", routeBtoA), NodeB);
            await AssertEndpointUpdateAsync(scenario, streamB, "node-b-shifted", routeBtoA);
            await scenario.ExpectTrafficAsync(proxyB, HttpPort, "A:/fleet", path: RequestPath);
            Assert.Equal(streamB, Assert.Single(scenario.Observations.Responses.Skip(beforePeerUpdate)).StreamId);
            Assert.Equal(2, scenario.Observations.Opened.Count);
            Assert.Single(scenario.Observations.Closed);

            await proxyB.StopAsync(scenario.Token);
            await scenario.UntilAsync(() => Watches(scenario, NodeB) == 0 && scenario.Observations.Closed.Count == 2,
                "node-b's independent stream and watches to close");
            var departureB = scenario.Observations.Closed.Last();
            Assert.Equal((streamB, NodeB), (departureB.StreamId, departureB.Node.Id));
            Assert.All(scenario.Observations.Requests, entry => Assert.Null(entry.Request.ErrorDetail));
        }, scenario.Token, peer: proxyB);
    });

    [Fact]
    public Task Ads_UsesTheSharedClusterKeyForIndependentFleetStreams() => RunAsync(async scenario =>
    {
        const string edgeOne = "edge-1";
        const string edgeTwo = "edge-2";
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
        var initial = Single("fleet-initial", backendA.Address);
        await scenario.PublishAsync(initial, FleetName);
        await using var first = CreateProxy(scenario, edgeOne, FleetName);
        await using var second = CreateProxy(scenario, edgeTwo, FleetName);

        await first.RunWithDiagnosticsAsync(output, async () =>
        {
            await Task.WhenAll(
                scenario.ExpectTrafficAsync(first, HttpPort, "A:/fleet", path: RequestPath),
                scenario.ExpectTrafficAsync(second, HttpPort, "A:/fleet", path: RequestPath));
            long firstStream = StreamForNode(scenario, edgeOne, FleetName);
            long secondStream = StreamForNode(scenario, edgeTwo, FleetName);
            Assert.NotEqual(firstStream, secondStream);
            Assert.Equal(2, scenario.Observations.Opened.Count);
            await Task.WhenAll(WaitForGraphAsync(scenario, initial, firstStream), WaitForGraphAsync(scenario, initial, secondStream));
            Assert.Equal([FleetName], scenario.Cache.GetStatusKeys());
            Assert.Null(scenario.Cache.GetStatusInfo(edgeOne));
            Assert.Null(scenario.Cache.GetStatusInfo(edgeTwo));
            await scenario.UntilAsync(() => Watches(scenario, FleetName) == 8, "four fleet watches from each proxy");

            int beforeUpdate = scenario.Observations.Responses.Count;
            var endpoints = Assignment(ServiceA, backendB.Address);
            await scenario.PublishAsync(Replace(initial, ResourceTypeUrls.Endpoint, "fleet-shifted", endpoints), FleetName);
            await Task.WhenAll(
                AssertEndpointUpdateAsync(scenario, firstStream, "fleet-shifted", endpoints),
                AssertEndpointUpdateAsync(scenario, secondStream, "fleet-shifted", endpoints),
                scenario.ExpectTrafficAsync(first, HttpPort, "B:/fleet", path: RequestPath),
                scenario.ExpectTrafficAsync(second, HttpPort, "B:/fleet", path: RequestPath));
            Assert.Equal(new[] { firstStream, secondStream }.Order(),
                scenario.Observations.Responses.Skip(beforeUpdate).Select(entry => entry.StreamId).Order());

            await first.StopAsync(scenario.Token);
            await scenario.UntilAsync(() => Watches(scenario, FleetName) == 4 && scenario.Observations.Closed.Count == 1,
                "only the departed proxy's watches to leave the shared fleet entry");
            Assert.Equal(firstStream, Assert.Single(scenario.Observations.Closed).StreamId);
            await scenario.ExpectTrafficAsync(second, HttpPort, "B:/fleet", path: RequestPath);
            await second.StopAsync(scenario.Token);
            await scenario.UntilAsync(() => Watches(scenario, FleetName) == 0 && scenario.Observations.Closed.Count == 2,
                "the remaining shared fleet watches to close");
            Assert.Equal(secondStream, scenario.Observations.Closed.Last().StreamId);
            Assert.All(scenario.Observations.Requests, entry => Assert.Null(entry.Request.ErrorDetail));
        }, scenario.Token, peer: second);
    }, nodeHasher: new ClusterNodeHasher());

    private static EnvoyProxyContainer CreateProxy(LiveEnvoyScenario scenario, string node, string cluster = "integration") =>
        scenario.Proxy(EnvoyProxyContainer.RenderAdsBootstrap(node, scenario.Host.GrpcPort, nodeCluster: cluster), HttpPort);

    private static long StreamForNode(LiveEnvoyScenario scenario, string node, string cluster = "integration") =>
        Assert.Single(scenario.Observations.Requests.Where(entry => entry.Request.Node?.Id == node
            && entry.Request.Node.Cluster == cluster).Select(entry => entry.StreamId).Distinct());

    private static int Watches(LiveEnvoyScenario scenario, string key) => scenario.Cache.GetStatusInfo(key)?.NumWatches ?? -1;

    private static Task WaitForGraphAsync(LiveEnvoyScenario scenario, Snapshot snapshot, long stream) =>
        scenario.UntilAsync(() => BasicTypes.All(type => scenario.HasAck(type, snapshot.GetVersion(type), stream)),
            $"the four snapshot ACKs on ADS stream {stream}");

    private static async Task AssertEndpointUpdateAsync(LiveEnvoyScenario scenario, long stream, string version,
        ClusterLoadAssignment expected)
    {
        await scenario.UntilAsync(() => scenario.HasAck(ResourceTypeUrls.Endpoint, version, stream),
            $"the endpoint ACK for {version} on ADS stream {stream}");
        var response = Assert.Single(scenario.Observations.Responses, entry => entry.StreamId == stream
            && entry.Response.TypeUrl == ResourceTypeUrls.Endpoint && entry.Response.VersionInfo == version);
        Assert.Equal(expected, Assert.Single(response.Response.Resources).Unpack<ClusterLoadAssignment>());
    }

    private sealed class ClusterNodeHasher : INodeHasher
    {
        public string Hash(Node? node) => node?.Cluster ?? "";
    }
}
