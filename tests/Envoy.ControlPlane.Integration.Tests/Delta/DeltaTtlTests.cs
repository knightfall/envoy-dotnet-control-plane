using System.Diagnostics;
using System.Net;
using Envoy.Config.Cluster.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Delta.DeltaAssertions;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Delta;

public sealed class DeltaTtlTests(ITestOutputHelper output)
{
    private const string NodeId = "delta-ttl";
    private const string RemovedStat = "cluster_manager.cluster_removed";

    [Fact]
    public Task ExplicitHeartbeatsExtendClusterLifetimeUntilStopped()
    {
        var ttl = TimeSpan.FromSeconds(3);
        DeltaTtlPublisher? publisher = null;
        return RunAsync(async scenario =>
        {
            await using var backend = scenario.Backend("TTL");
            await backend.StartAsync(scenario.Token);
            var initial = Single("ttl-initial", backend.Address);
            var cluster = (Cluster)initial.GetResources(ResourceTypeUrls.Cluster)![ServiceA]!;
            var groups = BasicTypes.ToDictionary(type => type, type => new ResourceCollection("ttl-initial",
                initial.GetResourcesAndTtl(type)!.Values), StringComparer.Ordinal);
            groups[ResourceTypeUrls.Cluster] = new ResourceCollection("ttl-initial", [new ResourceEntry(cluster, ttl)]);
            var snapshot = new Snapshot(groups);
            await scenario.PublishAsync(snapshot);

            await using var proxy = scenario.Proxy(scenario.AdsBootstrap(delta: true), HttpPort);
            using var stopHeartbeats = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
            Task heartbeats = publisher!.RunHeartbeatsAsync(stopHeartbeats.Token);
            try
            {
                await proxy.RunWithDiagnosticsAsync(output, async () =>
                {
                    await WaitForAcksAsync(scenario, "ttl-initial", BasicTypes);
                    await scenario.ExpectTrafficAsync(proxy, HttpPort, "TTL:/delta", path: RequestPath);
                    long removedBefore = await RemovedClustersAsync(proxy, scenario.Token);

                    long healthySince = Stopwatch.GetTimestamp();
                    do
                    {
                        Assert.Equal((HttpStatusCode.OK, "TTL:/delta"),
                            await scenario.ReadTrafficAsync(proxy, HttpPort, RequestPath, scenario.Token));
                        await Task.Delay(TimeSpan.FromMilliseconds(150), scenario.Token);
                    }
                    while (Stopwatch.GetElapsedTime(healthySince) <= ttl * 2.5);

                    await stopHeartbeats.CancelAsync();
                    await heartbeats;
                    var refreshes = Responses(scenario).Where(sent => sent.Response.TypeUrl == ResourceTypeUrls.Cluster
                        && sent.Response.SystemVersionInfo == "ttl-initial").ToArray();
                    Assert.True(refreshes.Length >= 4, "Expected the initial cluster and at least three heartbeat envelopes.");
                    var full = Assert.Single(refreshes, sent => sent.Response.Resources.Any(resource => resource.Resource_ is not null));
                    Assert.Equal(refreshes[0], full);
                    Assert.Equal(cluster, Assert.Single(full.Response.Resources).Resource_.Unpack<Cluster>());
                    string hash = snapshot.GetVersionMap(ResourceTypeUrls.Cluster)![ServiceA];
                    foreach (var (streamId, response) in refreshes)
                    {
                        Assert.Equal(full.StreamId, streamId);
                        Assert.Empty(response.RemovedResources);
                        var refresh = Assert.Single(response.Resources);
                        Assert.Equal(ServiceA, refresh.Name);
                        Assert.Equal(hash, refresh.Version);
                        Assert.Equal(ttl, refresh.Ttl.ToTimeSpan());
                    }
                    Assert.True(refreshes.Skip(1).Count(sent => Acked(scenario, sent)) >= 3);
                    Assert.Equal(refreshes.Length, refreshes.Select(sent => sent.Response.Nonce).Distinct().Count());

                    await scenario.ExpectTrafficAsync(proxy, HttpPort, expectedBody: null, HttpStatusCode.ServiceUnavailable, path: RequestPath);
                    Assert.True(await RemovedClustersAsync(proxy, scenario.Token) > removedBefore);
                    Assert.False(await HasClusterAsync(proxy, scenario.Token));
                    Assert.Same(snapshot, scenario.Cache.GetSnapshot(scenario.NodeId));
                    Assert.Equal(ttl, snapshot.GetResourcesAndTtl(ResourceTypeUrls.Cluster)![ServiceA].Ttl);
                    Assert.True(scenario.Cache.GetStatusInfo(scenario.NodeId)!.NumDeltaWatches > 0);
                    Assert.Single(Responses(scenario).Where(sent => sent.Response.TypeUrl == ResourceTypeUrls.Cluster)
                        .SelectMany(sent => sent.Response.Resources), resource => resource.Resource_ is not null);

                    var repairedCluster = cluster.Clone();
                    repairedCluster.ConnectTimeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(2));
                    var repaired = Replace(snapshot, ResourceTypeUrls.Cluster, "ttl-repair", repairedCluster);
                    await scenario.PublishAsync(repaired);
                    await WaitForAcksAsync(scenario, "ttl-repair", ResourceTypeUrls.Cluster);
                    var repair = Assert.Single(Assert.Single(Responses(scenario),
                        sent => sent.Response.SystemVersionInfo == "ttl-repair").Response.Resources);
                    Assert.Equal(repairedCluster, repair.Resource_.Unpack<Cluster>());
                    Assert.Null(repair.Ttl);
                    await scenario.ExpectTrafficAsync(proxy, HttpPort, "TTL:/delta", path: RequestPath);
                    Assert.True(await HasClusterAsync(proxy, scenario.Token));
                    Assert.True(heartbeats.IsCompletedSuccessfully);
                    await StopAndVerifyAsync(scenario, proxy);
                }, scenario.Token);
            }
            finally
            {
                await stopHeartbeats.CancelAsync();
                await heartbeats;
            }
        }, nodeId: NodeId, wrapCache: cache => publisher = new DeltaTtlPublisher(cache, NodeId, ServiceA, ttl));
    }

    private static async Task<long> RemovedClustersAsync(EnvoyProxyContainer proxy, CancellationToken cancellationToken)
    {
        var document = await proxy.GetStatsAsync(cancellationToken, filter: RemovedStat);
        return Assert.Single(document.Stats, stat => stat.Name == RemovedStat).Value.GetInt64();
    }

    private static async Task<bool> HasClusterAsync(EnvoyProxyContainer proxy, CancellationToken cancellationToken)
    {
        var dump = await proxy.GetConfigDumpAsync(cancellationToken);
        return AdminEntries(dump, "ClustersConfigDump", "dynamic_active_clusters")
            .Any(entry => entry.GetProperty("cluster").GetProperty("name").GetString() == ServiceA);
    }
}
