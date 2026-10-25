using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Example;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class PreviewResourceTests
{
    [Fact]
    public void ExampleGraph_UsesTypedExtensionsAndResolvesExactEdsRdsReferences()
    {
        var snapshot = ExampleResources.CreateSnapshot("preview/v1", 8080, 10000);
        snapshot.EnsureConsistent();
        var cluster = Assert.IsType<Cluster>(Assert.Single(snapshot.GetResources(ResourceTypeUrls.Cluster)!).Value);
        var listener = Assert.IsType<Listener>(Assert.Single(snapshot.GetResources(ResourceTypeUrls.Listener)!).Value);
        var route = Assert.IsType<RouteConfiguration>(Assert.Single(snapshot.GetResources(ResourceTypeUrls.Route)!).Value);
        var endpoints = Assert.IsType<ClusterLoadAssignment>(Assert.Single(snapshot.GetResources(ResourceTypeUrls.Endpoint)!).Value);
        Assert.Equal("service", ResourceCatalog.GetName(cluster));
        Assert.Equal("service", ResourceCatalog.GetName(endpoints));
        Assert.Equal("routes", route.Name);
        Assert.Equal("listener", listener.Name);
        Assert.Equal(10000U, listener.Address.SocketAddress.PortValue);
        Assert.NotNull(cluster.EdsClusterConfig.EdsConfig.Ads);
        Assert.Equal(Envoy.Config.Core.V3.ApiVersion.V3, cluster.EdsClusterConfig.EdsConfig.ResourceApiVersion);
        var filter = Assert.Single(Assert.Single(listener.FilterChains).Filters);
        Assert.Equal(WellKnownFilterNames.HttpConnectionManager, filter.Name);
        var manager = Assert.IsType<HttpConnectionManager>(ResourceReferences.GetHttpConnectionManager(filter));
        Assert.Equal(route.Name, manager.Rds.RouteConfigName);
        Assert.NotNull(manager.Rds.ConfigSource.Ads);
        Assert.Equal(Envoy.Config.Core.V3.ApiVersion.V3, manager.Rds.ConfigSource.ResourceApiVersion);
        var router = Assert.Single(manager.HttpFilters);
        Assert.Equal(WellKnownFilterNames.Router, router.Name);
        Assert.True(router.TypedConfig.TryUnpack<Router>(out _));
        Assert.Equal(cluster.Name, Assert.Single(Assert.Single(route.VirtualHosts).Routes).Route_.Cluster);
        var references = ResourceReferences.GetReferences(PreviewHelperCorpus.GraphTypes.SelectMany(type => snapshot.GetResourcesAndTtl(type)!.Values));
        Assert.Equal(2, references.Count);
        Assert.Equal("service", Assert.Single(references[ResourceTypeUrls.Endpoint]));
        Assert.Equal("routes", Assert.Single(references[ResourceTypeUrls.Route]));
    }

    [Fact]
    public void ReplacementGraph_RetainsPublishedEndpointAndListenerMessages()
    {
        var first = ExampleResources.CreateSnapshot("v1", 8080, 10000);
        var second = ExampleResources.CreateSnapshot("v2", 8081, 10001);
        var previousEndpoint = Assert.IsType<ClusterLoadAssignment>(first.GetResources(ResourceTypeUrls.Endpoint)!["service"]);
        var nextEndpoint = Assert.IsType<ClusterLoadAssignment>(second.GetResources(ResourceTypeUrls.Endpoint)!["service"]);
        Assert.Equal(8080U, Assert.Single(Assert.Single(previousEndpoint.Endpoints).LbEndpoints).Endpoint.Address.SocketAddress.PortValue);
        Assert.Equal(8081U, Assert.Single(Assert.Single(nextEndpoint.Endpoints).LbEndpoints).Endpoint.Address.SocketAddress.PortValue);
        Assert.Equal(10000U, Assert.IsType<Listener>(first.GetResources(ResourceTypeUrls.Listener)!["listener"]).Address.SocketAddress.PortValue);
        Assert.Equal(10001U, Assert.IsType<Listener>(second.GetResources(ResourceTypeUrls.Listener)!["listener"]).Address.SocketAddress.PortValue);
        Assert.All(PreviewHelperCorpus.GraphTypes, type => Assert.Equal("v1", first.GetVersion(type)));
        Assert.All(PreviewHelperCorpus.GraphTypes, type => Assert.Equal("v2", second.GetVersion(type)));
    }

    [Fact]
    public void HelperCorpus_MatchesDeclaredTypedAnyOutcomesAndExportsExampleGraph()
    {
        var fixtures = PreviewHelperCorpus.ReadFixtures();
        Assert.Equal(8, fixtures.Count);
        var cases = fixtures.Select(fixture => PreviewHelperCorpus.Evaluate(fixture!)).ToArray();
        Assert.Equal(8, cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        for (int index = 0; index < cases.Length; index++)
        {
            var expected = fixtures[index]!["expected"]!;
            Assert.Equal(expected["isHcm"]!.GetValue<bool>(), cases[index].IsHcm);
            Assert.Equal(expected["routeName"]?.GetValue<string>(), cases[index].RouteName);
        }
        PreviewHelperCorpus.Write(PreviewHelperCorpus.Report(cases));
    }
}
