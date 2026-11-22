using Buf.Validate;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Type.Matcher.V3;
using Envoy.Type.V3;
using ProtoValidate;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;
using RetryBudget = Envoy.Config.Cluster.V3.CircuitBreakers.Types.Thresholds.Types.RetryBudget;

namespace Envoy.ControlPlane.Integration.Tests.Fleet;

public sealed class ValidatedPublicationTests(ITestOutputHelper output)
{
    private const string ProbePath = "/validated";

    [Fact]
    public Task AppValidation_RejectsAnInvalidRouteBeforePublicationAndEnvoyRejectsAnUngatedUpdate() => RunAsync(async scenario =>
    {
        var validator = new Validator(new ValidatorOptions
        {
            FileDescriptors =
            [
                Cluster.Descriptor.File, ClusterLoadAssignment.Descriptor.File, Listener.Descriptor.File,
                RouteConfiguration.Descriptor.File, HttpConnectionManager.Descriptor.File, Router.Descriptor.File,
                ValueMatcher.Descriptor.File,
            ],
            PreLoadDescriptors = true,
            DisableLazy = true,
        });
        var retryBudget = new RetryBudget { BudgetPercent = new Percent { Value = 25 }, MinRetryConcurrency = 3 };
        var cluster = CreateCluster(ServiceA, Source(ads: true));
        cluster.CircuitBreakers = new CircuitBreakers
        {
            Thresholds = { new CircuitBreakers.Types.Thresholds { RetryBudget = retryBudget } },
        };
        var matcher = new ValueMatcher
        {
            OrMatch = new OrMatcher
            {
                ValueMatchers =
                {
                    new ValueMatcher { BoolMatch = true },
                    new ValueMatcher { ListMatch = new ListMatcher { OneOf = new ValueMatcher { PresentMatch = true } } },
                },
            },
        };
        var routes = Routes(RouteA, ServiceA);
        routes.VirtualHosts[0].Routes.Insert(0, new Route
        {
            Match = new RouteMatch
            {
                Prefix = "/metadata",
                DynamicMetadata =
                {
                    new MetadataMatcher
                    {
                        Filter = "integration.validation",
                        Path = { new MetadataMatcher.Types.PathSegment { Key = "enabled" } },
                        Value = matcher,
                    },
                },
            },
            Route_ = new RouteAction { Cluster = ServiceA },
        });

        // Both values are part of the live graph and also validate directly with lazy evaluator builds disabled.
        Assert.True(validator.Validate(retryBudget, failFast: false).IsSuccess);
        Assert.True(validator.Validate(matcher, failFast: false).IsSuccess);
        await using var backend = scenario.Backend("A");
        await backend.StartAsync(scenario.Token);
        var graph = Replace(Single("accepted", backend.Address), ResourceTypeUrls.Cluster, "accepted", cluster);
        var accepted = Replace(graph, ResourceTypeUrls.Route, "accepted", routes);
        Assert.Empty(await ValidateAndPublishAsync(validator, scenario, accepted));

        await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
        await proxy.RunWithDiagnosticsAsync(output, async () =>
        {
            await scenario.WaitForAcksAsync(accepted, BasicTypes);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/validated", path: ProbePath);
            int responseCount = scenario.Observations.Responses.Count;

            var invalidRoutes = routes.Clone();
            invalidRoutes.VirtualHosts[0].Domains.Clear();
            var invalid = Replace(accepted, ResourceTypeUrls.Route, "invalid-rds", invalidRoutes);
            var violations = await ValidateAndPublishAsync(validator, scenario, invalid);
            var domain = Assert.Single(violations);
            Assert.Equal("virtual_hosts[0].domains", domain.Field.GetPath());
            Assert.Equal("repeated.min_items", domain.RuleId);
            Assert.Same(accepted, scenario.Cache.GetSnapshot(scenario.NodeId));
            await Task.Delay(TimeSpan.FromSeconds(1), scenario.Token);
            Assert.Empty(scenario.Observations.Responses.Skip(responseCount));
            Assert.All(scenario.Observations.Requests, entry => Assert.Null(entry.Request.ErrorDetail));
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/validated", path: ProbePath);

            var repairedRoutes = routes.Clone();
            repairedRoutes.VirtualHosts[0].Routes[1].Route_.PrefixRewrite = "/repaired/";
            var repaired = Replace(accepted, ResourceTypeUrls.Route, "repaired-rds", repairedRoutes);
            var releaseNack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Host.Observations.SotwRequestHandler = (request, token) =>
                request.TypeUrl == ResourceTypeUrls.Route && request.ErrorDetail is not null
                    ? new ValueTask(releaseNack.Task.WaitAsync(token))
                    : ValueTask.CompletedTask;
            try
            {
                await scenario.PublishAsync(invalid);
                await scenario.UntilAsync(() => scenario.Observations.Requests.Any(entry => entry.Request.ErrorDetail is not null),
                    "the Envoy RDS NACK for the same candidate sent through the unvalidated cache");
                var rejected = Assert.Single(scenario.Observations.Responses.Skip(responseCount));
                var nack = Assert.Single(scenario.Observations.Requests, entry => entry.Request.ErrorDetail is not null);
                Assert.Equal(ResourceTypeUrls.Route, rejected.Response.TypeUrl);
                Assert.Equal("invalid-rds", rejected.Response.VersionInfo);
                Assert.Equal(invalidRoutes, Assert.Single(rejected.Response.Resources).Unpack<RouteConfiguration>());
                Assert.Equal(rejected.StreamId, nack.StreamId);
                Assert.Equal(rejected.Response.Nonce, nack.Request.ResponseNonce);
                Assert.Equal("accepted", nack.Request.VersionInfo);
                Assert.Contains("domain", nack.Request.ErrorDetail.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Same(invalid, scenario.Cache.GetSnapshot(scenario.NodeId));
                await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/validated", path: ProbePath);

                var repair = ValidateAndPublishAsync(validator, scenario, repaired);
                await scenario.UntilAsync(() => ReferenceEquals(repaired, scenario.Cache.GetSnapshot(scenario.NodeId)),
                    "the application to install the validated repair before releasing the NACK hook");
                releaseNack.TrySetResult();
                Assert.Empty(await repair);
            }
            finally
            {
                releaseNack.TrySetResult();
                scenario.Host.Observations.SotwRequestHandler = null;
            }

            await scenario.WaitForAcksAsync(repaired, [ResourceTypeUrls.Route]);
            await scenario.ExpectTrafficAsync(proxy, HttpPort, "A:/repaired/validated", path: ProbePath);
            Assert.Single(scenario.Observations.Requests, entry => entry.Request.ErrorDetail is not null);
            Assert.Single(scenario.Observations.Opened);
            await proxy.StopAsync(scenario.Token);
            await scenario.UntilAsync(() => scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumWatches: 0 }
                && scenario.Observations.Closed.Count == 1, "the validation case's ADS stream and watches to close");
        }, scenario.Token);
    }, nodeId: "validated-publisher");

    private static async Task<IReadOnlyList<Violation>> ValidateAndPublishAsync(Validator validator, LiveEnvoyScenario scenario, Snapshot snapshot)
    {
        snapshot.EnsureConsistent();
        var violations = new List<Violation>();
        foreach (string type in BasicTypes)
        {
            foreach (var resource in snapshot.GetResources(type)!.Values)
            {
                violations.AddRange(validator.Validate(resource!, failFast: false).Violations);
            }
        }
        // Explicitly validate the Any payloads that the application packs into this listener.
        var listener = (Listener)snapshot.GetResources(ResourceTypeUrls.Listener)![ListenerA]!;
        var manager = listener.FilterChains[0].Filters[0].TypedConfig.Unpack<HttpConnectionManager>();
        violations.AddRange(validator.Validate(manager, failFast: false).Violations);
        var router = manager.HttpFilters[0].TypedConfig.Unpack<Router>();
        violations.AddRange(validator.Validate(router, failFast: false).Violations);
        if (violations.Count == 0)
        {
            await scenario.PublishAsync(snapshot);
        }
        return violations;
    }
}
