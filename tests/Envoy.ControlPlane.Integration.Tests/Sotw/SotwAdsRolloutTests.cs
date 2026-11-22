using System.Globalization;
using System.Net;
using System.Text.Json;
using Envoy.Admin.V3;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Extensions.Filters.Network.TcpProxy.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;
using HttpProtocolOptions = Envoy.Extensions.Upstreams.Http.V3.HttpProtocolOptions;
using SocketAddress = Envoy.Config.Core.V3.SocketAddress;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Sotw;

public sealed class SotwAdsRolloutTests(ITestOutputHelper output)
{
    private static readonly JsonParser AdminParser = new(JsonParser.Settings.Default.WithTypeRegistry(TypeRegistry.FromMessages(
        Cluster.Descriptor, ClusterLoadAssignment.Descriptor, Listener.Descriptor, RouteConfiguration.Descriptor,
        ScopedRouteConfiguration.Descriptor, TypedExtensionConfig.Descriptor, HttpConnectionManager.Descriptor,
        Router.Descriptor, TcpProxy.Descriptor, HttpProtocolOptions.Descriptor)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Ads_ThreeGraphVersionsPreserveAcceptedTrafficAfterNackAndRepair(bool ordered) => RunAsync(async scenario =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await backendA.StartAsync(scenario.Token);
        await backendB.StartAsync(scenario.Token);
        var first = Rollout("rollout-v1", 1, backendA.Address, backendB.Address);
        var second = Rollout("rollout-v2", 2, backendB.Address, backendA.Address, "/second/");
        var third = Rollout("rollout-v3", 3, backendA.Address, backendB.Address, "/repaired/");
        await scenario.PublishAsync(first);

        string bootstrap = scenario.AdsBootstrap() + "\n" + $$"""
            layered_runtime:
              layers:
              - name: {{RuntimeLayer}}
                rtds_layer:
                  name: {{RuntimeLayer}}
                  rtds_config: { resource_api_version: V3, ads: {} }
            """;
        await using var proxy = scenario.Proxy(bootstrap, HttpPort, ScopedPort, TcpPort, ExtensionPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await CheckRolloutAsync(first, 1, "A", "B", backendA.Address, backendB.Address, "/");
            await scenario.ExpectTrafficAsync(proxy, ScopedPort, expectedBody: null, HttpStatusCode.NotFound);

            await scenario.PublishAsync(second);
            await CheckRolloutAsync(second, 2, "B", "A", backendB.Address, backendA.Address, "/second/");

            var invalid = Routes(RouteA, ServiceA, "/rejected/");
            invalid.VirtualHosts[0].Domains.Clear();
            var rejected = Replace(second, ResourceTypeUrls.Route, "invalid-rds", invalid, Routes(RouteB, ServiceB, "/second/"));
            var releaseNack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Pause the request hook so the rejected version cannot be resent while inspecting retained state.
            scenario.Host.Observations.SotwRequestHandler = (request, token) =>
                request.TypeUrl == ResourceTypeUrls.Route && request.ErrorDetail is not null
                    ? new ValueTask(releaseNack.Task.WaitAsync(token))
                    : ValueTask.CompletedTask;
            try
            {
                await scenario.PublishAsync(rejected);
                await scenario.UntilAsync(() => scenario.Observations.Requests.Any(entry =>
                    entry.Request.TypeUrl == ResourceTypeUrls.Route && entry.Request.ErrorDetail is not null), "RDS NACK");
                var nack = Assert.Single(scenario.Observations.Requests, entry =>
                    entry.Request.TypeUrl == ResourceTypeUrls.Route && entry.Request.ErrorDetail is not null);
                var rejectedResponse = Assert.Single(scenario.Observations.Responses, entry => entry.StreamId == nack.StreamId
                    && entry.Response.TypeUrl == ResourceTypeUrls.Route && entry.Response.VersionInfo == "invalid-rds");
                Assert.NotEmpty(nack.Request.ErrorDetail.Message);
                Assert.NotEmpty(rejectedResponse.Response.Nonce);
                Assert.Equal(rejectedResponse.Response.Nonce, nack.Request.ResponseNonce);
                Assert.Equal("rollout-v2", nack.Request.VersionInfo);
                Assert.Equal("invalid-rds", scenario.Cache.GetSnapshot(scenario.NodeId).GetVersion(ResourceTypeUrls.Route));
                await CheckRolloutAsync(second, 2, "B", "A", backendB.Address, backendA.Address, "/second/");

                // Publish can wait for response-channel capacity while the stream is paused.
                Task publication = scenario.PublishAsync(third);
                await scenario.UntilAsync(() => ReferenceEquals(third, scenario.Cache.GetSnapshot(scenario.NodeId)), "installation of the repair");
                releaseNack.TrySetResult();
                await publication;
            }
            finally
            {
                releaseNack.TrySetResult();
            }
            await CheckRolloutAsync(third, 3, "A", "B", backendA.Address, backendB.Address, "/repaired/");
            Assert.Equal(ResourceTypeUrls.Any, Assert.Single(scenario.Observations.Opened).TypeUrl);
        }, scenario.Token);

        async Task CheckRolloutAsync(Snapshot published, int runtimeValue, string serviceA, string serviceB,
            IPAddress endpointA, IPAddress endpointB, string rewrite)
        {
            await scenario.WaitForAcksAsync(published, RolloutTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, serviceA + ":" + rewrite + "sotw");
            await scenario.ExpectTrafficAsync(proxy, ScopedPort, serviceB + ":" + rewrite + "sotw", configureRequest: request => request.Headers.Add(ScopeHeader, Scope));
            await scenario.ExpectTrafficAsync(proxy, TcpPort, serviceB + ":/sotw");
            await scenario.ExpectTrafficAsync(proxy, ExtensionPort, serviceA + ":" + rewrite + "sotw");

            var dump = await proxy.GetConfigDumpAsync(scenario.Token, includeEndpoints: true);
            var clusters = ReadAdmin<ClustersConfigDump>(dump).DynamicActiveClusters
                .Select(config => config.Cluster.Unpack<Cluster>()).OrderBy(cluster => cluster.Name, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { ServiceA, ServiceB }, clusters.Select(cluster => cluster.Name));
            Assert.All(clusters, cluster => Assert.Equal(Cluster.Types.DiscoveryType.Eds, cluster.Type));
            var listeners = ReadAdmin<ListenersConfigDump>(dump).DynamicListeners;
            Assert.Equal(new[] { ListenerA, "listener-extension", "listener-scoped", "listener-tcp" },
                listeners.Where(listener => listener.ActiveState is not null).Select(listener => listener.Name).Order(StringComparer.Ordinal));
            var tcp = Assert.Single(listeners, listener => listener.Name == "listener-tcp").ActiveState.Listener.Unpack<Listener>();
            Assert.Equal(ServiceB, Assert.Single(Assert.Single(tcp.FilterChains).Filters).TypedConfig.Unpack<TcpProxy>().Cluster);

            var routes = ReadAdmin<RoutesConfigDump>(dump).DynamicRouteConfigs.ToDictionary(
                config => config.RouteConfig.Unpack<RouteConfiguration>().Name, StringComparer.Ordinal);
            Assert.Equal(2, routes.Count);
            foreach (string routeName in new[] { RouteA, RouteB })
            {
                var route = routes[routeName];
                Assert.Equal(published.GetVersion(ResourceTypeUrls.Route), route.VersionInfo);
                var routeAction = Assert.Single(Assert.Single(route.RouteConfig.Unpack<RouteConfiguration>().VirtualHosts).Routes).Route_;
                Assert.Equal(routeName == RouteA ? ServiceA : ServiceB, routeAction.Cluster);
                Assert.Equal(rewrite == "/" ? "" : rewrite, routeAction.PrefixRewrite);
            }
            var scoped = Assert.Single(ReadAdmin<ScopedRoutesConfigDump>(dump).DynamicScopedRouteConfigs);
            var scope = Assert.Single(scoped.ScopedRouteConfigs).Unpack<ScopedRouteConfiguration>();
            Assert.Equal(Scope, scope.Name);
            Assert.Equal(Scope, Assert.Single(scope.Key.Fragments).StringKey);
            Assert.Equal(RouteB, scope.RouteConfigurationName);

            var endpoints = ReadAdmin<EndpointsConfigDump>(dump).DynamicEndpointConfigs
                .Select(config => config.EndpointConfig.Unpack<ClusterLoadAssignment>())
                .ToDictionary(assignment => assignment.ClusterName, StringComparer.Ordinal);
            Assert.Equal(endpointA.ToString(), EndpointSocket(endpoints[ServiceA]).Address);
            Assert.Equal(endpointB.ToString(), EndpointSocket(endpoints[ServiceB]).Address);
            Assert.All(endpoints.Values, assignment => Assert.Equal((uint)EnvoyBackendContainer.Port, EndpointSocket(assignment).PortValue));

            var extension = Assert.Single(ReadAdmin<EcdsConfigDump>(dump).EcdsFilters);
            Assert.Equal(published.GetVersion(ResourceTypeUrls.ExtensionConfig), extension.VersionInfo);
            var dynamicConfig = extension.EcdsFilter.Unpack<TypedExtensionConfig>();
            Assert.Equal(ExtensionName, dynamicConfig.Name);
            var manager = dynamicConfig.TypedConfig.Unpack<HttpConnectionManager>();
            Assert.Equal(RouteA, manager.Rds.RouteConfigName);
            Assert.Equal("extension-" + published.GetVersion(ResourceTypeUrls.ExtensionConfig), manager.StatPrefix);

            var runtime = await proxy.GetRuntimeAsync(scenario.Token);
            Assert.Equal(runtimeValue.ToString(CultureInfo.InvariantCulture), runtime.Entries[RuntimeKey].FinalValue);
        }
    }, ordered: ordered);

    private static T ReadAdmin<T>(EnvoyConfigDump dump) where T : IMessage<T>, new()
    {
        string typeUrl = ResourceTypeUrls.Prefix + new T().Descriptor.FullName;
        var config = Assert.Single(dump.Configs, entry => entry.Type == typeUrl);
        return AdminParser.Parse<T>(JsonSerializer.Serialize(config.Fields));
    }

    private static SocketAddress EndpointSocket(ClusterLoadAssignment assignment) =>
        Assert.Single(Assert.Single(assignment.Endpoints).LbEndpoints).Endpoint.Address.SocketAddress;
}
