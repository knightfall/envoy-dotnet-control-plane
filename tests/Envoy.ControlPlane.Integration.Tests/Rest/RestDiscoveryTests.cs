using System.Collections.Concurrent;
using System.Net;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Extensions.TransportSockets.Tls.V3;
using Envoy.Service.Discovery.V3;
using Envoy.Service.Runtime.V3;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;

namespace Envoy.ControlPlane.Integration.Tests.Rest;

public sealed class RestDiscoveryTests(ITestOutputHelper output)
{
    private const string RequestPath = "/rest";
    private static readonly TypeRegistry GatewayTypes = TypeRegistry.FromMessages(
        Cluster.Descriptor, ClusterLoadAssignment.Descriptor, Listener.Descriptor, RouteConfiguration.Descriptor,
        ScopedRouteConfiguration.Descriptor, Secret.Descriptor, Runtime.Descriptor, TypedExtensionConfig.Descriptor,
        Resource.Descriptor, HttpConnectionManager.Descriptor, Router.Descriptor);
    private static readonly string[] FetchPaths = [ResourceFetchPaths.Clusters, ResourceFetchPaths.Endpoints,
        ResourceFetchPaths.Listeners, ResourceFetchPaths.Routes];

    [Fact]
    public Task Rest_PollsFourFamiliesConvergesToNotModifiedAndAppliesLiveUpdates() => RunAsync("rest-polling", async (scenario, polls) =>
    {
        await using var backendA = scenario.Backend("A");
        await using var backendB = scenario.Backend("B");
        await Task.WhenAll(backendA.StartAsync(scenario.Token), backendB.StartAsync(scenario.Token));
        var initial = Single("rest-initial", backendA.Address, RestSource());
        await scenario.PublishAsync(initial);
        await using var proxy = CreateProxy(scenario);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/rest", path: RequestPath);
            await scenario.UntilAsync(() => FetchPaths.All(path => polls.Any(poll =>
                poll.Path == path && poll.Status == StatusCodes.Status304NotModified)), "304 convergence for every REST xDS family");

            foreach (string path in FetchPaths)
            {
                var observed = polls.Where(poll => poll.Path == path).ToArray();
                Assert.Equal(StatusCodes.Status200OK, observed[0].Status);
                Assert.Equal("application/json", observed[0].ContentType);
                Assert.All(observed, poll =>
                {
                    Assert.Equal("POST", poll.Method);
                    Assert.Equal("HTTP/1.1", poll.Protocol);
                    Assert.Contains(poll.Status, new[] { StatusCodes.Status200OK, StatusCodes.Status304NotModified });
                });
                Assert.Contains(observed.Skip(1), poll => poll.Status == StatusCodes.Status304NotModified);
            }

            var wire = scenario.Host.Observations;
            Assert.All(BasicTypes, type =>
            {
                Assert.Contains(wire.FetchRequests, request => request.TypeUrl == type && request.Node.Id == scenario.NodeId
                    && request.VersionInfo == "rest-initial");
                Assert.Contains(wire.FetchResponses, fetched => fetched.Response?.TypeUrl == type
                    && fetched.Response.VersionInfo == "rest-initial");
            });
            var listener = Assert.Single(wire.FetchResponses, fetched => fetched.Response?.TypeUrl == ResourceTypeUrls.Listener
                && fetched.Response.VersionInfo == "rest-initial").Response!;
            var filter = Assert.Single(Assert.Single(Assert.Single(listener.Resources).Unpack<Listener>().FilterChains).Filters);
            var manager = filter.TypedConfig.Unpack<HttpConnectionManager>();
            Assert.Equal(RouteA, manager.Rds.RouteConfigName);
            Assert.NotNull(Assert.Single(manager.HttpFilters).TypedConfig.Unpack<Router>());

            int previousPolls = polls.Count;
            var endpointUpdate = Replace(initial, ResourceTypeUrls.Endpoint, "rest-eds", Assignment(ServiceA, backendB.Address));
            var update = Replace(endpointUpdate, ResourceTypeUrls.Route, "rest-rds", Routes(RouteA, ServiceA, "/new/"));
            await scenario.PublishAsync(update);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "B:/new/rest", path: RequestPath);
            await scenario.UntilAsync(() => wire.FetchRequests.Any(request => request.TypeUrl == ResourceTypeUrls.Endpoint
                && request.VersionInfo == "rest-eds") && wire.FetchRequests.Any(request => request.TypeUrl == ResourceTypeUrls.Route
                && request.VersionInfo == "rest-rds"), "REST polls with the newly accepted endpoint and route versions");

            var followups = polls.Skip(previousPolls).ToArray();
            Assert.Contains(followups, poll => poll.Path == ResourceFetchPaths.Endpoints && poll.Status == StatusCodes.Status200OK);
            Assert.Contains(followups, poll => poll.Path == ResourceFetchPaths.Routes && poll.Status == StatusCodes.Status200OK);
            Assert.DoesNotContain(followups, poll => poll.Status >= StatusCodes.Status400BadRequest);
            Assert.Empty(wire.Sotw.Opened);
            Assert.Empty(wire.Delta.Opened);
            await proxy.StopAsync(scenario.Token);
        }, scenario.Token);
    });

    [Fact]
    public Task Rest_HostStoppingCancelsAnActiveEnvoyPollAndReturns503() => RunAsync("rest-stopping", async (scenario, polls) =>
    {
        await using var backend = scenario.Backend("A");
        await backend.StartAsync(scenario.Token);
        await scenario.PublishAsync(Single("rest-initial", backend.Address, RestSource()));
        await using var proxy = CreateProxy(scenario);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/rest", path: RequestPath);
            var entered = new TaskCompletionSource<HttpContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource<(bool ApplicationStopping, bool ClientDisconnected)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var unreleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Host.Observations.FetchRequestHandler = async (context, request) =>
            {
                if (request.TypeUrl != ResourceTypeUrls.Route || request.VersionInfo != "rest-initial"
                    || context.State is not HttpContext http || !entered.TrySetResult(http))
                {
                    return;
                }
                try
                {
                    await unreleased.Task.WaitAsync(context.CancellationToken);
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    var lifetime = http.RequestServices.GetRequiredService<IHostApplicationLifetime>();
                    canceled.TrySetResult((lifetime.ApplicationStopping.IsCancellationRequested, http.RequestAborted.IsCancellationRequested));
                    throw;
                }
            };
            try
            {
                var activePoll = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), scenario.Token);
                string trace = activePoll.TraceIdentifier;
                Assert.Equal(ResourceFetchPaths.Routes, activePoll.Request.Path.Value);
                Assert.False(activePoll.RequestAborted.IsCancellationRequested);
                Assert.DoesNotContain(polls, poll => poll.Trace == trace);

                await scenario.Host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), scenario.Token);
                Assert.Equal((true, false), await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), scenario.Token));
                Assert.False(unreleased.Task.IsCompleted);
                var completion = Assert.Single(polls, poll => poll.Trace == trace);
                Assert.Equal(StatusCodes.Status503ServiceUnavailable, completion.Status);
                const string upstream503 = "cluster." + EnvoyProxyContainer.RestCluster + ".upstream_rq_503";
                await scenario.UntilAsync(async token =>
                {
                    var stats = await proxy.GetStatsAsync(token, upstream503);
                    return stats.Stats.Any(stat => stat.Name == upstream503 && stat.Value.GetInt64() > 0);
                }, "Envoy to receive the shut-down poll's HTTP 503");
                Assert.DoesNotContain(polls, poll => poll.Status is StatusCodes.Status400BadRequest or StatusCodes.Status500InternalServerError);
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/rest", path: RequestPath);
                await proxy.StopAsync(scenario.Token);
            }
            finally
            {
                unreleased.TrySetResult();
                scenario.Host.Observations.FetchRequestHandler = null;
            }
        }, scenario.Token);
    });

    private static Task RunAsync(string nodeId, Func<LiveEnvoyScenario, ConcurrentQueue<HttpPoll>, Task> test)
    {
        var polls = new ConcurrentQueue<HttpPoll>();
        return LiveEnvoyScenario.RunAsync(scenario => test(scenario, polls), ads: false, nodeId: nodeId,
            createHost: watcher => new HostControlPlaneFixture(watcher, enableHttpGateway: true, httpGatewayTypes: GatewayTypes,
                configureApplication: app => app.Use(next => async context =>
                {
                    await next(context);
                    polls.Enqueue(new HttpPoll(context.TraceIdentifier, context.Request.Path.Value ?? "",
                        context.Request.Method, context.Request.Protocol, context.Response.StatusCode, context.Response.ContentType));
                })));
    }

    private static EnvoyProxyContainer CreateProxy(LiveEnvoyScenario scenario) =>
        scenario.Proxy(EnvoyProxyContainer.RenderRestBootstrap(scenario.NodeId, scenario.Host.HttpGatewayPort!.Value), HttpPort);

    private static ConfigSource RestSource() => new()
    {
        ResourceApiVersion = ApiVersion.V3,
        ApiConfigSource = new ApiConfigSource
        {
            ApiType = ApiConfigSource.Types.ApiType.Rest,
            TransportApiVersion = ApiVersion.V3,
            ClusterNames = { EnvoyProxyContainer.RestCluster },
            RefreshDelay = Duration.FromTimeSpan(EnvoyProxyContainer.RestRefreshDelay),
            RequestTimeout = Duration.FromTimeSpan(EnvoyProxyContainer.RestRequestTimeout),
        },
    };

    private sealed record HttpPoll(string Trace, string Path, string Method, string Protocol, int Status, string? ContentType);
}
