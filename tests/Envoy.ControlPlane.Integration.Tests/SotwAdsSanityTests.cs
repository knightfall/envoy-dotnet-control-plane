using System.Net;
using DotNet.Testcontainers.Builders;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Example;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envoy.ControlPlane.Integration.Tests;

public sealed class SotwAdsSanityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Proxy_RoutesSotwAdsTrafficThroughContainerBackend_AndClosesStream()
    {
        const string node = "integration-node";
        const string backendName = "integration-backend";
        const string version = "sanity-v1";
        const int listenerPort = 10000;
        var pollTimeout = TimeSpan.FromSeconds(20);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var cancellationToken = deadline.Token;

        var images = EnvoyImageLock.Load();
        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync(cancellationToken);
        await using var backend = new EnvoyBackendContainer(images, network, backendName);
        await backend.StartAsync(cancellationToken);

        var cache = new SnapshotCache(ads: true);
        await using var host = new HostControlPlaneFixture(cache,
            configureServices: services => services.AddSingleton(new SotwOptions { OrderedAds = true }));
        await host.StartAsync(cancellationToken);
        int managementPort = host.GrpcPort;
        Assert.InRange(managementPort, 1, 65535);
        await host.RestartAsync(cancellationToken);
        Assert.Equal(managementPort, host.GrpcPort);

        await cache.SetSnapshotAsync(node, CreateSnapshot(version, backend.Address, listenerPort), new ResponseContext(cancellationToken));

        string bootstrap = EnvoyProxyContainer.RenderAdsBootstrap(node, managementPort);
        await using var proxy = new EnvoyProxyContainer(images, network, bootstrap, listenerPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            var sotw = host.Observations.Sotw;
            string[] types = [ResourceTypeUrls.Cluster, ResourceTypeUrls.Endpoint, ResourceTypeUrls.Listener, ResourceTypeUrls.Route];
            await AsyncPoll.UntilAsync(() => types.All(type => HasAck(sotw, type, version)), "Envoy SotW ACKs",
                pollTimeout, cancellationToken);

            using var client = new HttpClient();
            const string requestPath = "/health/sanity?source=ads";
            var requestUri = new Uri(proxy.ListenerAddress(listenerPort), requestPath);
            await AsyncPoll.UntilAsync(async token =>
            {
                try
                {
                    using var response = await client.GetAsync(requestUri, token);
                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal(backendName + ":" + requestPath, await response.Content.ReadAsStringAsync(token));
                    return true;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
            }, "HTTP traffic through the EDS backend", pollTimeout, cancellationToken);

            var dump = await proxy.GetConfigDumpAsync(cancellationToken);
            Assert.Contains(dump.Configs, config => config.Type == "type.googleapis.com/envoy.admin.v3.ListenersConfigDump"
                && config.Fields.ContainsKey("dynamic_listeners"));
            var stats = await proxy.GetStatsAsync(cancellationToken, "^cluster[.]service[.]upstream_cx_total$");
            Assert.Contains(stats.Stats, stat => stat.Name == "cluster.service.upstream_cx_total" && stat.Value.GetInt64() > 0);

            var stream = Assert.Single(sotw.Opened);
            Assert.Empty(stream.TypeUrl);
            await proxy.StopAsync(cancellationToken);
            await AsyncPoll.UntilAsync(() => sotw.Closed.Any(), "the ADS stream to close", pollTimeout, cancellationToken);
            var closed = Assert.Single(sotw.Closed);
            Assert.Equal(stream.StreamId, closed.StreamId);
            Assert.Equal(node, closed.Node.Id);
        }, cancellationToken);
    }

    private static Snapshot CreateSnapshot(string version, IPAddress backendAddress, int listenerPort)
    {
        var snapshot = ExampleResources.CreateSnapshot(version, EnvoyBackendContainer.Port, (uint)listenerPort);
        var endpoint = (ClusterLoadAssignment)snapshot.GetResources(ResourceTypeUrls.Endpoint)!["service"]!;
        endpoint.Endpoints[0].LbEndpoints[0].Endpoint.Address.SocketAddress.Address = backendAddress.ToString();
        var listener = (Listener)snapshot.GetResources(ResourceTypeUrls.Listener)!["listener"]!;
        listener.Address.SocketAddress.Address = IPAddress.Any.ToString();
        return snapshot;
    }

    private static bool HasAck(StreamObservations<DiscoveryRequest, DiscoveryResponse> observations, string type, string version) =>
        observations.Responses.Any(sent => sent.Response.TypeUrl == type && sent.Response.VersionInfo == version
            && sent.Response.Nonce.Length > 0 && observations.Requests.Any(received => received.StreamId == sent.StreamId
                && received.Request.TypeUrl == type && received.Request.VersionInfo == version
                && received.Request.ResponseNonce == sent.Response.Nonce && received.Request.ErrorDetail is null));
}
