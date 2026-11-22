using System.Globalization;
using System.Net;
using System.Text;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;
using DeltaAssertions = Envoy.ControlPlane.Integration.Tests.Delta.DeltaAssertions;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;

namespace Envoy.ControlPlane.Integration.Tests.Mux;

public sealed class MuxDiscoveryTests(ITestOutputHelper output)
{
    private const string SnapshotVersion = "snapshot-initial";
    private const string CollectionPrefix = "linear-";
    private const string RequestPath = "/mux";
    private const string Standby = "unused-standby";
    private const string Staged = "unused-staged";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public Task Mux_RoutesEdsToLinearCacheAcrossProtocolsAndStreamLayouts(bool ads, bool delta)
    {
        var linear = new LinearCache(ResourceTypeUrls.Endpoint, versionPrefix: CollectionPrefix);
        string nodeId = $"mux-eds-{(ads ? "ads" : "split")}-{(delta ? "delta" : "sotw")}";
        return LiveEnvoyScenario.RunAsync(async scenario =>
        {
            await using var backendA = scenario.Backend("A");
            await using var backendB = scenario.Backend("B");
            await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
            var a = Assignment(ServiceA, backendA.Address);
            var b = Assignment(ServiceA, backendB.Address);
            await linear.SetResourcesAsync(new Dictionary<string, IMessage?>
            {
                [ServiceA] = a,
                [Standby] = Assignment(Standby, backendB.Address),
            }, scenario.Token);
            // Publish a different EDS address in the default child to prove Envoy uses the LinearCache result.
            var snapshot = Single(SnapshotVersion, backendB.Address, ads, delta);
            await scenario.PublishAsync(snapshot);
            string bootstrap = ads ? scenario.AdsBootstrap(delta) : scenario.SplitBootstrap(delta);
            await using var proxy = scenario.Proxy(bootstrap, HttpPort);
            await proxy.RunWithDiagnosticsAsync(output, async () =>
            {
                string[] snapshotTypes = [ResourceTypeUrls.Cluster, ResourceTypeUrls.Listener, ResourceTypeUrls.Route];
                if (delta)
                {
                    await DeltaAssertions.WaitForAcksAsync(scenario, SnapshotVersion, snapshotTypes);
                    Assert.Equal(ads ? 1 : 4, scenario.Host.Observations.Delta.Opened.Count);
                }
                else
                {
                    await scenario.WaitForAcksAsync(snapshot, snapshotTypes);
                    Assert.Equal(ads ? 1 : 4, scenario.Host.Observations.Sotw.Opened.Count);
                }
                await VerifyAssignmentAsync(1, a, "A:/mux");

                await linear.UpdateResourceAsync(ServiceA, b, scenario.Token);
                await VerifyAssignmentAsync(2, b, "B:/mux");

                await linear.UpdateResourcesAsync(new Dictionary<string, IMessage?>
                {
                    [ServiceA] = a,
                    [Staged] = Assignment(Staged, backendA.Address),
                }, [Standby], scenario.Token);
                Assert.Equal(new[] { ServiceA, Staged }.Order(), linear.GetResources().Keys.Order());
                await VerifyAssignmentAsync(3, a, "A:/mux");

                await linear.SetResourcesAsync(Only(b), scenario.Token);
                Assert.Equal(ServiceA, Assert.Single(linear.GetResources()).Key);
                await VerifyAssignmentAsync(4, b, "B:/mux");

                int responseCount = scenario.Host.Observations.Sotw.Responses.Count;
                await linear.DeleteResourceAsync(ServiceA, scenario.Token);
                Assert.Empty(linear.GetResources());
                if (delta)
                {
                    await DeltaAssertions.WaitForAcksAsync(scenario, CollectionPrefix + "5", ResourceTypeUrls.Endpoint);
                    var removal = Assert.Single(scenario.Host.Observations.Delta.Responses, sent =>
                        sent.Response.TypeUrl == ResourceTypeUrls.Endpoint && sent.Response.SystemVersionInfo == CollectionPrefix + "5");
                    DeltaAssertions.AssertRemoval(removal.Response, ServiceA);
                }
                else
                {
                    // SotW EDS delivers changed assignments. Delete-only leaves the watch pending.
                    await Task.Delay(DeltaAssertions.QuietWindow, scenario.Token);
                    Assert.Empty(scenario.Host.Observations.Sotw.Responses.Skip(responseCount));
                }

                // Envoy 1.40.1 preserves existing EDS hosts when it receives only a tombstone.
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "B:/mux", path: RequestPath);
                await linear.UpdateResourceAsync(ServiceA, new ClusterLoadAssignment { ClusterName = ServiceA }, scenario.Token);
                await WaitForEdsWatchAsync(6);
                await scenario.ExpectTrafficAsync(proxy, HttpPort, expectedBody: null, HttpStatusCode.ServiceUnavailable, path: RequestPath);
                const string membership = "cluster." + ServiceA + ".membership_total";
                var stats = await proxy.GetStatsAsync(scenario.Token, membership);
                Assert.Equal(0, Assert.Single(stats.Stats, stat => stat.Name == membership).Value.GetInt64());

                await linear.UpdateResourceAsync(ServiceA, a, scenario.Token);
                await VerifyAssignmentAsync(7, a, "A:/mux");
                Assert.Same(snapshot, scenario.Cache.GetSnapshot(scenario.NodeId));

                if (delta)
                {
                    await DeltaAssertions.StopAndVerifyAsync(scenario, proxy);
                }
                else
                {
                    await StopSotwAsync(scenario, proxy);
                }
                Assert.Equal(0, linear.NumCacheWatches);

                async Task VerifyAssignmentAsync(int version, ClusterLoadAssignment expected, string expectedTraffic)
                {
                    string collectionVersion = await WaitForEdsWatchAsync(version);
                    ClusterLoadAssignment received;
                    if (delta)
                    {
                        var message = Assert.Single(scenario.Host.Observations.Delta.Responses, sent =>
                            sent.Response.TypeUrl == ResourceTypeUrls.Endpoint && sent.Response.SystemVersionInfo == collectionVersion).Response;
                        Assert.Empty(message.RemovedResources);
                        var resource = Assert.Single(message.Resources);
                        Assert.Equal(ServiceA, resource.Name);
                        received = resource.Resource_.Unpack<ClusterLoadAssignment>();
                    }
                    else
                    {
                        var message = Assert.Single(scenario.Host.Observations.Sotw.Responses, sent =>
                            sent.Response.TypeUrl == ResourceTypeUrls.Endpoint && sent.Response.VersionInfo == collectionVersion).Response;
                        received = Assert.Single(message.Resources).Unpack<ClusterLoadAssignment>();
                    }
                    Assert.Equal(expected, received);
                    await scenario.ExpectTrafficAsync(proxy, HttpPort, expectedTraffic, path: RequestPath);
                }

                async Task<string> WaitForEdsWatchAsync(int version)
                {
                    string collectionVersion = CollectionPrefix + version.ToString(CultureInfo.InvariantCulture);
                    if (delta)
                    {
                        await DeltaAssertions.WaitForAcksAsync(scenario, collectionVersion, ResourceTypeUrls.Endpoint);
                    }
                    else
                    {
                        await scenario.UntilAsync(() => scenario.HasAck(ResourceTypeUrls.Endpoint, collectionVersion), "the SotW EDS ACK");
                    }
                    await scenario.UntilAsync(() => linear.NumWatches(ServiceA) == 1, "the replacement linear EDS watch");
                    return collectionVersion;
                }
            }, scenario.Token);
        }, ads: ads, nodeId: nodeId, wrapCache: snapshot => CreateMux(snapshot, linear, ResourceTypeUrls.Endpoint));
    }

    [Fact]
    public Task Mux_RoutesCdsToLinearCacheAndDelegatesFetchToTheSelectedChild()
    {
        var cluster = CreateCluster(ServiceA, Source(ads: true));
        var linear = new LinearCache(ResourceTypeUrls.Cluster, new Dictionary<string, IMessage?> { [ServiceA] = cluster }, CollectionPrefix);
        MuxCache? mux = null;
        return LiveEnvoyScenario.RunAsync(async scenario =>
        {
            await using var backend = scenario.Backend("A");
            await backend.StartAsync(scenario.Token);
            var snapshot = Single(SnapshotVersion, backend.Address);
            await scenario.PublishAsync(snapshot);
            await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
            await proxy.RunWithDiagnosticsAsync(output, async () =>
            {
                await scenario.UntilAsync(() => scenario.HasAck(ResourceTypeUrls.Cluster, CollectionPrefix + "0")
                    && linear.NumWildcardWatches == 1, "the initial CDS ACK and linear collection watch");
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/mux", path: RequestPath);

                await linear.DeleteResourceAsync(ServiceA, scenario.Token);
                await scenario.UntilAsync(() => scenario.HasAck(ResourceTypeUrls.Cluster, CollectionPrefix + "1"),
                    "the SotW CDS deletion ACK");
                var response = Assert.Single(scenario.Host.Observations.Sotw.Responses, sent =>
                    sent.Response.TypeUrl == ResourceTypeUrls.Cluster && sent.Response.VersionInfo == CollectionPrefix + "1");
                Assert.Empty(response.Response.Resources);
                await scenario.ExpectTrafficAsync(proxy, HttpPort, expectedBody: null, HttpStatusCode.ServiceUnavailable, path: RequestPath);
                await scenario.UntilAsync(() => linear.NumWildcardWatches == 1, "CDS watch replacement after deletion");
                await linear.UpdateResourceAsync(ServiceA, cluster, scenario.Token);
                await scenario.UntilAsync(() => scenario.HasAck(ResourceTypeUrls.Cluster, CollectionPrefix + "2"), "the SotW CDS re-add ACK");
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/mux", path: RequestPath);

                await VerifyFetchAsync(scenario, mux!);
                await StopSotwAsync(scenario, proxy);
                Assert.Equal(0, linear.NumCacheWatches);
            }, scenario.Token);
        }, nodeId: "mux-cds", wrapCache: snapshot => mux = CreateMux(snapshot, linear, ResourceTypeUrls.Cluster),
            createHost: watcher => new HostControlPlaneFixture(watcher, enableHttpGateway: true));
    }

    private static async Task VerifyFetchAsync(LiveEnvoyScenario scenario, MuxCache mux)
    {
        using var client = new HttpClient { BaseAddress = new UriBuilder("http", "127.0.0.1", scenario.Host.HttpGatewayPort!.Value).Uri };
        var request = new DiscoveryRequest { Node = new Node { Id = scenario.NodeId }, ResourceNames = { RouteA } };
        using var routesBody = new StringContent(JsonFormatter.Default.Format(request), Encoding.UTF8, "application/json");
        using var routes = await client.PostAsync(ResourceFetchPaths.Routes, routesBody, scenario.Token);
        Assert.Equal(HttpStatusCode.OK, routes.StatusCode);
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(TypeRegistry.FromMessages(RouteConfiguration.Descriptor)));
        var received = parser.Parse<DiscoveryResponse>(await routes.Content.ReadAsStringAsync(scenario.Token));
        Assert.Equal(ResourceTypeUrls.Route, received.TypeUrl);
        Assert.Equal(SnapshotVersion, received.VersionInfo);
        Assert.Equal(Routes(RouteA, ServiceA), Assert.Single(received.Resources).Unpack<RouteConfiguration>());

        request.ResourceNames.Clear();
        using var clustersBody = new StringContent(JsonFormatter.Default.Format(request), Encoding.UTF8, "application/json");
        using var clusters = await client.PostAsync(ResourceFetchPaths.Clusters, clustersBody, scenario.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, clusters.StatusCode);
        request.TypeUrl = ResourceTypeUrls.Cluster;
        var unsupported = await Assert.ThrowsAsync<NotSupportedException>(() => mux.FetchAsync(request));
        Assert.Contains(nameof(LinearCache), unsupported.Message, StringComparison.Ordinal);
    }

    private static MuxCache CreateMux(SnapshotCache snapshot, LinearCache linear, string linearType)
    {
        string Classify(string type) => type == linearType ? "linear" : "snapshot";
        return new MuxCache(new Dictionary<string, IConfigWatcher> { ["linear"] = linear, ["snapshot"] = snapshot },
            request => Classify(request.TypeUrl), request => Classify(request.TypeUrl));
    }

    private static Dictionary<string, IMessage?> Only(ClusterLoadAssignment endpoints) =>
        new(StringComparer.Ordinal) { [ServiceA] = endpoints };

    private static async Task StopSotwAsync(LiveEnvoyScenario scenario, EnvoyProxyContainer proxy)
    {
        await proxy.StopAsync(scenario.Token);
        await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumWatches: 0 }
            && scenario.Observations.Closed.Count == scenario.Observations.Opened.Count, "all SotW watches and streams to close");
        Assert.Equal(scenario.Observations.Opened.Select(stream => stream.StreamId).Order(),
            scenario.Observations.Closed.Select(stream => stream.StreamId).Order());
        Assert.All(scenario.Observations.Requests, received => Assert.Null(received.Request.ErrorDetail));
    }
}
