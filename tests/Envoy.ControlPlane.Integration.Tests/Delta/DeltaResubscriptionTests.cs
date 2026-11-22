using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Delta.DeltaAssertions;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;

namespace Envoy.ControlPlane.Integration.Tests.Delta;

public sealed class DeltaResubscriptionTests(ITestOutputHelper output)
{
    private const int SecondPort = 10001;
    private const string NodeId = "delta-resubscription";
    private const string InitialVersion = "two-services";

    [Fact]
    public Task Ads_ReplacesWatchesOnUnsubscribeAndRedeliversExactNamesOnResubscribe()
    {
        Action<DeltaDiscoveryRequest>? onWatch = null;
        return LiveEnvoyScenario.RunAsync(RunAsync, nodeId: NodeId,
            wrapCache: cache => new ObservedWatcher(cache, request => onWatch?.Invoke(request)));

        async Task RunAsync(LiveEnvoyScenario scenario)
        {
            await using var backendA = scenario.Backend("A");
            await using var backendB = scenario.Backend("B");
            await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
            var source = Source(ads: true);
            var clusterA = CreateCluster(ServiceA, source);
            var clusterB = CreateCluster(ServiceB, source);
            var endpointA = Assignment(ServiceA, backendA.Address);
            var endpointB = Assignment(ServiceB, backendB.Address);
            var listenerA = CreateListener(ListenerA, HttpPort, HttpFilter(HttpManager(RouteA, source)));
            var listenerB = CreateListener("listener-b", SecondPort, HttpFilter(HttpManager(RouteB, source)));
            var routesA = Routes(RouteA, ServiceA);
            var routesB = Routes(RouteB, ServiceB);
            var initial = new Snapshot(InitialVersion, new Dictionary<string, IEnumerable<IMessage?>>
            {
                [ResourceTypeUrls.Cluster] = [clusterA, clusterB],
                [ResourceTypeUrls.Endpoint] = [endpointA, endpointB],
                [ResourceTypeUrls.Listener] = [listenerA, listenerB],
                [ResourceTypeUrls.Route] = [routesA, routesB],
            });
            initial.EnsureConsistent();
            await scenario.PublishAsync(initial);
            await using var proxy = scenario.Proxy(scenario.AdsBootstrap(delta: true), [HttpPort, SecondPort], listenerDrainSeconds: 1);
            await proxy.RunWithDiagnosticsAsync(output, async () =>
            {
                await WaitForAcksAsync(scenario, InitialVersion, BasicTypes);
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/delta", path: RequestPath);
                await scenario.ExpectTrafficAsync(proxy, SecondPort, "B:/delta", path: RequestPath);
                var wire = scenario.Host.Observations.Delta;
                long stream = Assert.Single(wire.Opened).StreamId;
                var initialEndpoint = InitialDelivery<ClusterLoadAssignment>(ResourceTypeUrls.Endpoint, ServiceB, endpointB);
                var initialRoute = InitialDelivery<RouteConfiguration>(ResourceTypeUrls.Route, RouteB, routesB);
                Assert.Contains(wire.Requests, received => received.StreamId == stream && received.Request.TypeUrl == ResourceTypeUrls.Endpoint
                    && received.Request.ResourceNamesSubscribe.Contains(ServiceB));
                Assert.Contains(wire.Requests, received => received.StreamId == stream && received.Request.TypeUrl == ResourceTypeUrls.Route
                    && received.Request.ResourceNamesSubscribe.Contains(RouteB));
                await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumDeltaWatches: 4 },
                    "the original EDS and RDS watches after all initial resources are ACKed");

                int unsubscribeStart = wire.Requests.Count;
                var replacements = new ConcurrentQueue<(string Type, bool TombstoneAlreadySent)>();
                onWatch = request =>
                {
                    string? name = request.TypeUrl switch
                    {
                        ResourceTypeUrls.Endpoint => ServiceB,
                        ResourceTypeUrls.Route => RouteB,
                        _ => null,
                    };
                    if (name is not null && request.ResourceNamesUnsubscribe.Contains(name))
                    {
                        replacements.Enqueue((request.TypeUrl, wire.Responses.Any(sent => sent.StreamId == stream
                            && sent.Response.TypeUrl == request.TypeUrl && sent.Response.RemovedResources.Contains(name))));
                    }
                };
                scenario.Host.Observations.DeltaRequestHandler = PublishRemovalDuringReplacementAsync;
                try
                {
                    // Removing the CDS resource detaches service-b's EDS subscription. Its listener still serves 503.
                    await scenario.PublishAsync(Replace(initial, ResourceTypeUrls.Cluster, "cds-detached", clusterA));
                    await WaitForAcksAsync(scenario, "cds-detached", ResourceTypeUrls.Cluster);
                    await scenario.ExpectTrafficAsync(proxy, SecondPort, expectedBody: null, HttpStatusCode.ServiceUnavailable, path: RequestPath);
                    await AssertUnsubscribeAndTombstoneAsync(ResourceTypeUrls.Endpoint, ServiceB, "eds-tombstone");

                    // Removing the listener releases route-b while route-a keeps the RDS discovery stream in use.
                    var afterEndpoint = (Snapshot)scenario.Cache.GetSnapshot(scenario.NodeId);
                    await scenario.PublishAsync(Replace(afterEndpoint, ResourceTypeUrls.Listener, "lds-detached", listenerA));
                    await WaitForAcksAsync(scenario, "lds-detached", ResourceTypeUrls.Listener);
                    await AssertUnsubscribeAndTombstoneAsync(ResourceTypeUrls.Route, RouteB, "rds-tombstone");
                    await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/delta", path: RequestPath);
                }
                finally
                {
                    scenario.Host.Observations.DeltaRequestHandler = null;
                }

                Assert.Equal([(ResourceTypeUrls.Endpoint, true), (ResourceTypeUrls.Route, true)], replacements);

                int resubscribeStart = wire.Requests.Count;
                int resendStart = wire.Responses.Count;
                var restored = WithSystemVersion(initial, "two-services-restored");
                await scenario.PublishAsync(restored);
                await VerifyRedeliveryAsync(ResourceTypeUrls.Endpoint, ServiceB, endpointB, initialEndpoint.Version);
                await VerifyRedeliveryAsync(ResourceTypeUrls.Route, RouteB, routesB, initialRoute.Version);
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/delta", path: RequestPath);
                await scenario.ExpectTrafficAsync(proxy, SecondPort, "B:/delta", path: RequestPath);
                Assert.Single(wire.Opened);
                Assert.Empty(wire.Closed);
                await StopAndVerifyAsync(scenario, proxy);

                Resource InitialDelivery<T>(string type, string name, T expected) where T : IMessage, new()
                {
                    var sent = Assert.Single(wire.Responses, observed => observed.StreamId == stream && observed.Response.TypeUrl == type
                        && observed.Response.Resources.Any(resource => resource.Name == name));
                    Assert.True(Acked(scenario, sent));
                    var resource = Assert.Single(sent.Response.Resources, resource => resource.Name == name);
                    Assert.Equal(expected, resource.Resource_.Unpack<T>());
                    return resource;
                }

                async ValueTask PublishRemovalDuringReplacementAsync(DeltaDiscoveryRequest request, CancellationToken cancellationToken)
                {
                    if (request.TypeUrl == ResourceTypeUrls.Endpoint && request.ResourceNamesUnsubscribe.Contains(ServiceB))
                    {
                        // The old watch still has to deliver this removal before the unsubscribe clears its known version.
                        cancellationToken.ThrowIfCancellationRequested();
                        var current = (Snapshot)scenario.Cache.GetSnapshot(scenario.NodeId);
                        await scenario.PublishAsync(Replace(current, ResourceTypeUrls.Endpoint, "eds-tombstone", endpointA));
                    }
                    else if (request.TypeUrl == ResourceTypeUrls.Route && request.ResourceNamesUnsubscribe.Contains(RouteB))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var current = (Snapshot)scenario.Cache.GetSnapshot(scenario.NodeId);
                        await scenario.PublishAsync(Replace(current, ResourceTypeUrls.Route, "rds-tombstone", routesA));
                    }
                }

                async Task AssertUnsubscribeAndTombstoneAsync(string type, string name, string version)
                {
                    await WaitForAcksAsync(scenario, version, type);
                    var received = Assert.Single(wire.Requests.Skip(unsubscribeStart), observed => observed.StreamId == stream
                        && observed.Request.TypeUrl == type && observed.Request.ResourceNamesUnsubscribe.Contains(name));
                    Assert.Equal([name], received.Request.ResourceNamesUnsubscribe);
                    var sent = Assert.Single(wire.Responses, observed => observed.Response.TypeUrl == type
                        && observed.Response.SystemVersionInfo == version);
                    Assert.Equal(stream, sent.StreamId);
                    AssertRemoval(sent.Response, name);
                }

                async Task VerifyRedeliveryAsync<T>(string type, string name, T expected, string priorHash) where T : IMessage, new()
                {
                    await WaitForAcksAsync(scenario, "two-services-restored", type);
                    var received = Assert.Single(wire.Requests.Skip(resubscribeStart), observed => observed.StreamId == stream
                        && observed.Request.TypeUrl == type && observed.Request.ResourceNamesSubscribe.Contains(name));
                    Assert.Equal([name], received.Request.ResourceNamesSubscribe);
                    Assert.Empty(received.Request.InitialResourceVersions);
                    var sent = Assert.Single(wire.Responses.Skip(resendStart), observed => observed.Response.TypeUrl == type);
                    Assert.Equal(stream, sent.StreamId);
                    Assert.Empty(sent.Response.RemovedResources);
                    var resource = Assert.Single(sent.Response.Resources);
                    Assert.Equal(name, resource.Name);
                    Assert.Equal(priorHash, resource.Version);
                    Assert.Equal(expected, resource.Resource_.Unpack<T>());
                }
            }, scenario.Token);
        }
    }

    private sealed class ObservedWatcher(SnapshotCache cache, Action<DeltaDiscoveryRequest> onWatch) : IConfigWatcher
    {
        public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses) =>
            cache.CreateWatch(request, subscription, responses);

        public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
        {
            onWatch(request);
            return cache.CreateDeltaWatch(request, subscription, responses);
        }
    }
}
