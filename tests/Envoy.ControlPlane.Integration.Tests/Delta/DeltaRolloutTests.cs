using System.Net;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Delta.DeltaAssertions;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;

namespace Envoy.ControlPlane.Integration.Tests.Delta;

public sealed class DeltaRolloutTests(ITestOutputHelper output)
{
    [Fact]
    public Task Ads_IsolatesChangesAndReconnectsWithAcceptedResourceVersions() => LiveEnvoyScenario.RunAsync(async scenario =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
        var initial = Single("ads-initial", backendA.Address);
        await scenario.PublishAsync(initial);
        await using var proxy = scenario.Proxy(scenario.AdsBootstrap(delta: true), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await WaitForAcksAsync(scenario, "ads-initial", BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/delta", path: RequestPath);
            var wire = scenario.Host.Observations.Delta;
            var stream = Assert.Single(wire.Opened);
            Assert.Empty(stream.TypeUrl);
            AssertInitialVersions(scenario, initial, _ => stream.StreamId);
            Assert.Equal(4, wire.Responses.Select(sent => sent.Response.Nonce).Distinct().Count());

            var routes = Routes(RouteA, ServiceA, "/changed/");
            var updated = Replace(initial, ResourceTypeUrls.Route, "ads-route", routes);
            await VerifyIsolatedRouteUpdateAsync(scenario, proxy, initial, updated, "ads-route");

            var removed = Replace(updated, ResourceTypeUrls.Cluster, "ads-removed");
            await scenario.PublishAsync(removed);
            await WaitForAcksAsync(scenario, "ads-removed", ResourceTypeUrls.Cluster);
            AssertRemoval(Assert.Single(wire.Responses, sent => sent.Response.SystemVersionInfo == "ads-removed").Response, ServiceA);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, expectedBody: null, HttpStatusCode.ServiceUnavailable, path: RequestPath);

            var cluster = (Cluster)initial.GetResources(ResourceTypeUrls.Cluster)![ServiceA]!;
            var restored = Replace(removed, ResourceTypeUrls.Cluster, "ads-restored", cluster);
            await scenario.PublishAsync(restored);
            await WaitForAcksAsync(scenario, "ads-restored", ResourceTypeUrls.Cluster);
            var restoredResource = Assert.Single(Assert.Single(wire.Responses,
                sent => sent.Response.SystemVersionInfo == "ads-restored").Response.Resources);
            Assert.Equal(cluster, restoredResource.Resource_.Unpack<Cluster>());
            Assert.Equal(initial.GetVersionMap(ResourceTypeUrls.Cluster)![ServiceA], restoredResource.Version);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/changed/delta", path: RequestPath);

            await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumDeltaWatches: 4 },
                "four live ADS Delta watches before shutdown");
            int port = scenario.Host.GrpcPort;
            await scenario.Host.StopAsync();
            Assert.Single(wire.Closed);
            Assert.Equal(0, scenario.Cache.GetStatusInfo(scenario.NodeId)!.NumDeltaWatches);
            int reconnectStart = wire.Requests.Count;
            int reconnectResponses = wire.Responses.Count;
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/changed/delta", path: RequestPath);
            await scenario.Host.StartAsync(scenario.Token);
            Assert.Equal(port, scenario.Host.GrpcPort);
            await scenario.UntilAsync(() => BasicTypes.All(type => wire.Requests.Skip(reconnectStart).Any(
                observed => observed.Request.TypeUrl == type && observed.Request.ResponseNonce.Length == 0)),
                "the first request for each Delta type after reconnect");
            var reconnect = Assert.Single(wire.Opened.Skip(1));
            foreach (string type in BasicTypes)
            {
                var first = Assert.Single(wire.Requests.Skip(reconnectStart), observed =>
                    observed.Request.TypeUrl == type && observed.Request.ResponseNonce.Length == 0);
                Assert.Equal(reconnect.StreamId, first.StreamId);
                Assert.Equal(restored.GetVersionMap(type)!.OrderBy(pair => pair.Key),
                    first.Request.InitialResourceVersions.OrderBy(pair => pair.Key));
            }
            await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumDeltaWatches: 4 },
                "four re-established Delta watches");
            Assert.All(wire.Responses.Skip(reconnectResponses), sent =>
            {
                Assert.Empty(sent.Response.Resources);
                Assert.Empty(sent.Response.RemovedResources);
            });

            var next = Replace(restored, ResourceTypeUrls.Endpoint, "ads-reconnected", Assignment(ServiceA, backendB.Address));
            await scenario.PublishAsync(next);
            await scenario.UntilAsync(() => wire.Responses.Skip(reconnectResponses).Any(sent => sent.StreamId == reconnect.StreamId
                && sent.Response.TypeUrl == ResourceTypeUrls.Endpoint && sent.Response.SystemVersionInfo == "ads-reconnected"
                && Acked(scenario, sent, reconnectStart)), "an ACK on the reconnected ADS stream for the EDS update");
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "B:/changed/delta", path: RequestPath);
            await StopAndVerifyAsync(scenario, proxy);
        }, scenario.Token);
    }, nodeId: "delta-ads");

    [Fact]
    public Task Split_UsesIndependentStreamsAndNoncesAndIgnoresVersionOnlyUpdates() => LiveEnvoyScenario.RunAsync(async scenario =>
    {
        await using var backend = scenario.Backend("A");
        await backend.StartAsync(scenario.Token);
        var initial = Single("split-initial", backend.Address, ads: false, delta: true);
        await scenario.PublishAsync(initial);
        await using var proxy = scenario.Proxy(scenario.SplitBootstrap(delta: true), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await WaitForAcksAsync(scenario, "split-initial", BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/delta", path: RequestPath);

            var wire = scenario.Host.Observations.Delta;
            Assert.Equal(BasicTypes.Order(), wire.Opened.Select(stream => stream.TypeUrl).Order());
            Assert.Equal(4, wire.Opened.Select(stream => stream.StreamId).Distinct().Count());
            AssertInitialVersions(scenario, initial, type => Assert.Single(wire.Opened, stream => stream.TypeUrl == type).StreamId);
            Assert.All(wire.Responses, sent => Assert.Equal("1", sent.Response.Nonce));

            var updated = Replace(initial, ResourceTypeUrls.Route, "split-route", Routes(RouteA, ServiceA, "/changed/"));
            var routeUpdate = await VerifyIsolatedRouteUpdateAsync(scenario, proxy, initial, updated, "split-route");
            Assert.Equal("2", routeUpdate.Nonce);

            int beforeEndpoint = wire.Responses.Count;
            var endpoints = Assignment(ServiceA, backend.Address);
            endpoints.Endpoints[0].LbEndpoints[0].LoadBalancingWeight = 2;
            var endpointChange = Replace(updated, ResourceTypeUrls.Endpoint, "split-eds", endpoints);
            await scenario.PublishAsync(endpointChange);
            await WaitForAcksAsync(scenario, "split-eds", ResourceTypeUrls.Endpoint);
            var endpointUpdate = Assert.Single(wire.Responses.Skip(beforeEndpoint));
            Assert.Equal(ResourceTypeUrls.Endpoint, endpointUpdate.Response.TypeUrl);
            Assert.Equal("split-eds", endpointUpdate.Response.SystemVersionInfo);
            Assert.Equal(Assert.Single(wire.Opened, stream => stream.TypeUrl == ResourceTypeUrls.Endpoint).StreamId, endpointUpdate.StreamId);
            Assert.Equal("2", endpointUpdate.Response.Nonce);
            Assert.Equal(endpoints, Assert.Single(endpointUpdate.Response.Resources).Resource_.Unpack<ClusterLoadAssignment>());
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/changed/delta", path: RequestPath);
            await StopAndVerifyAsync(scenario, proxy);
        }, scenario.Token);
    }, ads: false, nodeId: "delta-split");

    private static void AssertInitialVersions(LiveEnvoyScenario scenario, Snapshot snapshot, Func<string, long> streamForType)
    {
        var responses = Responses(scenario).ToArray();
        Assert.Equal(4, responses.Length);
        foreach (string type in BasicTypes)
        {
            var observed = Assert.Single(responses, sent => sent.Response.TypeUrl == type);
            Assert.Equal(streamForType(type), observed.StreamId);
            Assert.Empty(observed.Response.RemovedResources);
            var resource = Assert.Single(observed.Response.Resources);
            Assert.Equal(snapshot.GetVersionMap(type)![resource.Name], resource.Version);
            Assert.NotNull(resource.Resource_);
        }
    }

    private static async Task<DeltaDiscoveryResponse> VerifyIsolatedRouteUpdateAsync(
        LiveEnvoyScenario scenario, EnvoyProxyContainer proxy, Snapshot initial, Snapshot updated, string version)
    {
        var wire = scenario.Host.Observations.Delta;
        var previous = Assert.Single(wire.Responses, sent => sent.Response.TypeUrl == ResourceTypeUrls.Route);
        int beforeUpdate = wire.Responses.Count;
        await scenario.PublishAsync(updated);
        await WaitForAcksAsync(scenario, version, ResourceTypeUrls.Route);
        await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/changed/delta", path: RequestPath);
        await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumDeltaWatches: 4 },
            "four watches ready for a version-only publication");
        await Task.Delay(QuietWindow, scenario.Token);

        var change = Assert.Single(wire.Responses.Skip(beforeUpdate));
        Assert.Equal(previous.StreamId, change.StreamId);
        Assert.Equal(ResourceTypeUrls.Route, change.Response.TypeUrl);
        Assert.NotEqual(previous.Response.Nonce, change.Response.Nonce);
        Assert.Empty(change.Response.RemovedResources);
        var resource = Assert.Single(change.Response.Resources);
        Assert.Equal(RouteA, resource.Name);
        Assert.NotEqual(initial.GetVersionMap(ResourceTypeUrls.Route)![RouteA], resource.Version);
        Assert.Equal(updated.GetVersionMap(ResourceTypeUrls.Route)![RouteA], resource.Version);
        Assert.Equal((RouteConfiguration)updated.GetResources(ResourceTypeUrls.Route)![RouteA]!,
            resource.Resource_.Unpack<RouteConfiguration>());

        var bumped = WithSystemVersion(updated, version + "-new-system-version");
        await scenario.PublishAsync(bumped);
        Assert.Same(bumped, scenario.Cache.GetSnapshot(scenario.NodeId));
        foreach (string type in BasicTypes)
        {
            Assert.NotEqual(updated.GetVersion(type), bumped.GetVersion(type));
            foreach (var (name, payload) in updated.GetResources(type)!)
            {
                Assert.Equal(payload!.ToByteString(), bumped.GetResources(type)![name]!.ToByteString());
                Assert.Equal(updated.GetVersionMap(type)![name], bumped.GetVersionMap(type)![name]);
            }
        }
        await AssertQuietAsync(scenario, beforeUpdate + 1);
        await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/changed/delta", path: RequestPath);
        return change.Response;
    }
}
