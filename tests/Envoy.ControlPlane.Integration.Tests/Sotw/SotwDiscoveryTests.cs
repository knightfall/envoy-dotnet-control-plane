using System.Net;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.ControlPlane.Resources;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Sotw;

public sealed class SotwDiscoveryTests(ITestOutputHelper output)
{
    [Fact]
    public Task SplitXds_PublishesOnFourServicesAndSwitchesBackend() => RunAsync(async scenario =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await backendA.StartAsync(scenario.Token);
        await backendB.StartAsync(scenario.Token);
        var initial = Single("split-initial", backendA.Address, ads: false);
        await scenario.PublishAsync(initial);
        await using var proxy = scenario.Proxy(scenario.SplitBootstrap(), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.WaitForAcksAsync(initial, BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/sotw");
            foreach (string type in BasicTypes)
            {
                var stream = Assert.Single(scenario.Observations.Opened, entry => entry.TypeUrl == type);
                Assert.Contains(scenario.Observations.Requests, entry => entry.StreamId == stream.StreamId && entry.Request.TypeUrl == type);
                Assert.Contains(scenario.Observations.Responses, entry => entry.StreamId == stream.StreamId
                    && entry.Response.TypeUrl == type && entry.Response.VersionInfo == "split-initial");
                Assert.True(scenario.HasAck(type, "split-initial", stream.StreamId), type);
            }
            Assert.Equal(BasicTypes.Length, scenario.Observations.Opened.Select(entry => entry.StreamId).Distinct().Count());

            var update = Replace(initial, ResourceTypeUrls.Endpoint, "split-endpoint-b", Assignment(ServiceA, backendB.Address));
            await scenario.PublishAsync(update);
            await scenario.WaitForAcksAsync(update, [ResourceTypeUrls.Endpoint]);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "B:/sotw");
        }, scenario.Token);
    }, ads: false);

    [Fact]
    public Task Ads_RejectedFirstOpenClosesAndEnvoyRetries() => RunAsync(async scenario =>
    {
        scenario.Host.Observations.SotwOpenHandler = (streamId, _, _) => streamId == 1
            ? ValueTask.FromException(new RpcException(new Status(StatusCode.InvalidArgument, "first ADS stream rejected")))
            : ValueTask.CompletedTask;
        await using var backend = scenario.Backend("retry");
        await backend.StartAsync(scenario.Token);
        var snapshot = Single("retry-initial", backend.Address);
        await scenario.PublishAsync(snapshot);
        await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.WaitForAcksAsync(snapshot, BasicTypes);
            var closed = Assert.Single(scenario.Observations.Closed, entry => entry.StreamId == 1);
            Assert.Empty(closed.Node.Id);
            Assert.Contains(scenario.Observations.Opened, entry => entry.StreamId == 1 && entry.TypeUrl.Length == 0);
            Assert.Contains(scenario.Observations.Opened, entry => entry.StreamId == 2 && entry.TypeUrl.Length == 0);
            Assert.DoesNotContain(scenario.Observations.Requests, entry => entry.StreamId == 1);
            Assert.DoesNotContain(scenario.Observations.Responses, entry => entry.StreamId == 1);
            Assert.All(BasicTypes, type => Assert.True(scenario.HasAck(type, "retry-initial", streamId: 2), type));
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "retry:/sotw");
        }, scenario.Token);
    });

    [Fact]
    public Task Ads_CdsOnlyRepairKeepsEdsVersionAndActivatesCluster() => RunAsync(async scenario =>
    {
        await using var backend = scenario.Backend("http1", http1Only: true);
        await backend.StartAsync(scenario.Token);
        var baseline = Single("stable-eds", backend.Address);
        var incompatible = Replace(baseline, ResourceTypeUrls.Cluster, "incompatible-cds",
            CreateCluster(ServiceA, Source(ads: true), useHttp2: true));
        await scenario.PublishAsync(incompatible);
        await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.WaitForAcksAsync(incompatible, BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, expectedBody: null, HttpStatusCode.BadGateway);

            var repair = Replace(incompatible, ResourceTypeUrls.Cluster, "compatible-cds", CreateCluster(ServiceA, Source(ads: true)));
            Assert.All(BasicTypes.Where(type => type != ResourceTypeUrls.Cluster),
                type => Assert.Equal(incompatible.GetVersion(type), repair.GetVersion(type)));
            Assert.Same(incompatible.GetResources(ResourceTypeUrls.Endpoint)![ServiceA],
                repair.GetResources(ResourceTypeUrls.Endpoint)![ServiceA]);
            await scenario.PublishAsync(repair);
            await scenario.WaitForAcksAsync(repair, [ResourceTypeUrls.Cluster]);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "http1:/sotw");

            var dump = await proxy.GetConfigDumpAsync(scenario.Token);
            Assert.Contains(AdminEntries(dump, "ClustersConfigDump", "dynamic_active_clusters"), cluster =>
                cluster.GetProperty("version_info").GetString() == "compatible-cds"
                && cluster.GetProperty("cluster").GetProperty("name").GetString() == ServiceA);
            Assert.Empty(AdminEntries(dump, "ClustersConfigDump", "dynamic_warming_clusters"));
        }, scenario.Token);
    });

    [Fact]
    public Task Ads_PartialSnapshotServesKnownEdsWithoutSynthesizingMissingAssignment() => RunAsync(async scenario =>
    {
        const string missing = "service-no-eds";
        await using var backend = scenario.Backend("partial");
        await backend.StartAsync(scenario.Token);
        var baseline = Single("partial-eds", backend.Address);
        var auxiliarySource = Source(ads: true);
        auxiliarySource.InitialFetchTimeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(1));
        var partial = Replace(baseline, ResourceTypeUrls.Cluster, "partial-cds",
            CreateCluster(ServiceA, Source(ads: true)), CreateCluster(missing, auxiliarySource));
        await scenario.PublishAsync(partial);
        await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.WaitForAcksAsync(partial, BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "partial:/sotw");
            var cds = Assert.Single(scenario.Observations.Responses, entry => entry.Response.TypeUrl == ResourceTypeUrls.Cluster);
            Assert.Equal(new[] { ServiceA, missing }, cds.Response.Resources.Select(resource => resource.Unpack<Cluster>().Name).Order(StringComparer.Ordinal));
            Assert.Contains(scenario.Observations.Requests, entry => entry.Request.TypeUrl == ResourceTypeUrls.Endpoint
                && entry.Request.ResourceNames.Contains(ServiceA) && entry.Request.ResourceNames.Contains(missing));
            Assert.All(scenario.Observations.Responses.Where(entry => entry.Response.TypeUrl == ResourceTypeUrls.Endpoint),
                entry =>
                {
                    var assignment = Assert.Single(entry.Response.Resources).Unpack<ClusterLoadAssignment>();
                    Assert.Equal(ServiceA, assignment.ClusterName);
                });
        }, scenario.Token);
    });

    [Fact]
    public Task Ads_HostRestartRetainsTrafficAndResubscribesWithAcceptedVersions() => RunAsync(async scenario =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await backendA.StartAsync(scenario.Token);
        await backendB.StartAsync(scenario.Token);
        var initial = Single("before-restart", backendA.Address);
        await scenario.PublishAsync(initial);
        await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.WaitForAcksAsync(initial, BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/sotw");
            var originalStream = Assert.Single(scenario.Observations.Opened);
            int managementPort = scenario.Host.GrpcPort;
            await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumWatches: > 0 }, "active SotW watches");
            await scenario.Host.StopAsync();
            Assert.Equal(originalStream.StreamId, Assert.Single(scenario.Observations.Closed).StreamId);
            Assert.Equal(0, scenario.Cache.GetStatusInfo(scenario.NodeId)!.NumWatches);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/sotw");

            int requestCount = scenario.Observations.Requests.Count;
            await scenario.Host.StartAsync(scenario.Token);
            Assert.Equal(managementPort, scenario.Host.GrpcPort);
            await scenario.UntilAsync(() => BasicTypes.All(type => scenario.Observations.Requests.Skip(requestCount).Any(entry =>
                entry.Request.TypeUrl == type && entry.Request.VersionInfo == initial.GetVersion(type)
                && entry.Request.ResponseNonce.Length == 0)), "empty-nonce resubscription with accepted versions");

            var updated = Replace(initial, ResourceTypeUrls.Endpoint, "after-restart", Assignment(ServiceA, backendB.Address));
            await scenario.PublishAsync(updated);
            await scenario.WaitForAcksAsync(updated, [ResourceTypeUrls.Endpoint]);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "B:/sotw");
        }, scenario.Token);
    });
}
