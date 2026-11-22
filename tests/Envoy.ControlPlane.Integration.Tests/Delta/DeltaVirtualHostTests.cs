using System.Net;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Http.OnDemand.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Delta.DeltaAssertions;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;

namespace Envoy.ControlPlane.Integration.Tests.Delta;

public sealed class DeltaVirtualHostTests(ITestOutputHelper output)
{
    private const string Authority = "on-demand.example";
    private const string VirtualHostName = RouteA + "/" + Authority;
    private const string VirtualHostType = ResourceTypeUrls.VirtualHost;

    [Fact]
    public Task SplitVhds_LooksUpAuthorityUpdatesRouteAndRemovesVirtualHost() => LiveEnvoyScenario.RunAsync(async scenario =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
        var source = Source(ads: false, delta: true);
        var manager = HttpManager(RouteA, source);
        manager.HttpFilters.Insert(0, new HttpFilter { Name = "envoy.filters.http.on_demand", TypedConfig = Any.Pack(new OnDemand()) });
        var initial = new Snapshot("vhds-initial", new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Cluster] = [CreateCluster(ServiceA, source), CreateCluster(ServiceB, source)],
            [ResourceTypeUrls.Endpoint] = [Assignment(ServiceA, backendA.Address), Assignment(ServiceB, backendB.Address)],
            [ResourceTypeUrls.Listener] = [CreateListener(ListenerA, HttpPort, HttpFilter(manager))],
            [ResourceTypeUrls.Route] = [new RouteConfiguration { Name = RouteA, Vhds = new Vhds { ConfigSource = source } }],
            [VirtualHostType] = [],
        });
        initial.EnsureConsistent();
        await scenario.PublishAsync(initial);

        await using var proxy = scenario.Proxy(scenario.SplitBootstrap(delta: true), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await WaitForAcksAsync(scenario, "vhds-initial", BasicTypes);
            var wire = scenario.Host.Observations.Delta;
            var stream = Assert.Single(wire.Opened, opened => opened.TypeUrl == VirtualHostType);
            Assert.DoesNotContain(wire.Requests, received => received.StreamId == stream.StreamId
                && received.Request.ResourceNamesSubscribe.Contains(VirtualHostName));

            using var lookupCancellation = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
            var lookup = scenario.ReadTrafficAsync(proxy, HttpPort, RequestPath, lookupCancellation.Token, SetHost);
            try
            {
                await scenario.UntilAsync(() => wire.Requests.Any(received => received.StreamId == stream.StreamId
                    && received.Request.ResourceNamesSubscribe.Contains(VirtualHostName)), "the authority lookup on DeltaVirtualHosts");
                var request = Assert.Single(wire.Requests, received => received.StreamId == stream.StreamId
                    && received.Request.ResourceNamesSubscribe.Contains(VirtualHostName)).Request;
                Assert.Equal(VirtualHostType, request.TypeUrl);
                Assert.Equal([VirtualHostName], request.ResourceNamesSubscribe);
                Assert.Empty(request.ResourceNamesUnsubscribe);

                await PublishVirtualHostAsync("vhds-A", ServiceA);
                Assert.Equal((HttpStatusCode.OK, "A:/delta"), await lookup);
                await PublishVirtualHostAsync("vhds-B", ServiceB);
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "B:/delta", path: RequestPath, configureRequest: SetHost);

                var removed = Replace((Snapshot)scenario.Cache.GetSnapshot(scenario.NodeId), VirtualHostType, "vhds-removed");
                await scenario.PublishAsync(removed);
                await WaitForAcksAsync(scenario, "vhds-removed", VirtualHostType);
                var tombstone = Assert.Single(wire.Responses, sent => sent.Response.SystemVersionInfo == "vhds-removed");
                Assert.Equal(stream.StreamId, tombstone.StreamId);
                AssertRemoval(tombstone.Response, VirtualHostName);
                await scenario.UntilAsync(async token =>
                {
                    var (status, _) = await scenario.ReadTrafficAsync(proxy, HttpPort, RequestPath, token, SetHost);
                    return status is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable;
                }, "a 404 or 503 after removal of the virtual host");
                await StopAndVerifyAsync(scenario, proxy);
            }
            finally
            {
                await lookupCancellation.CancelAsync();
                await ((Task)lookup).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            async Task PublishVirtualHostAsync(string version, string cluster)
            {
                var virtualHost = Routes(VirtualHostName, cluster).VirtualHosts[0];
                virtualHost.Domains.Clear();
                virtualHost.Domains.Add(Authority);
                var snapshot = Replace(initial, VirtualHostType, version, virtualHost);
                await scenario.PublishAsync(snapshot);
                await WaitForAcksAsync(scenario, version, VirtualHostType);
                var sent = Assert.Single(wire.Responses, item => item.Response.SystemVersionInfo == version);
                Assert.Equal(stream.StreamId, sent.StreamId);
                Assert.Empty(sent.Response.RemovedResources);
                var resource = Assert.Single(sent.Response.Resources);
                Assert.Equal(VirtualHostName, resource.Name);
                Assert.Equal(snapshot.GetVersionMap(VirtualHostType)![VirtualHostName], resource.Version);
                Assert.Equal(virtualHost, resource.Resource_.Unpack<VirtualHost>());
            }
        }, scenario.Token);
    }, ads: false, nodeId: "delta-vhds");

    private static void SetHost(HttpRequestMessage request) => request.Headers.Host = Authority;
}
