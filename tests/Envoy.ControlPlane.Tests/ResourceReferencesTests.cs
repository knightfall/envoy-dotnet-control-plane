using Envoy.Config.Cluster.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using ListenerFilter = Envoy.Config.Listener.V3.Filter;

namespace Envoy.ControlPlane.Tests;

public sealed class ResourceReferencesTests
{
    [Fact]
    public void EdsReferences_UseServiceOverrideOrClusterName()
    {
        var references = ResourceReferences.GetReferences([
            new(ResourceFixtures.EdsCluster("cluster", "service")), new(ResourceFixtures.EdsCluster("fallback")),
            new(ResourceFixtures.EdsCluster("duplicate", "service"))]);
        Assert.Equal(ResourceTypeUrls.Endpoint, Assert.Single(references).Key);
        Assert.Equal(new[] { "fallback", "service" }, references[ResourceTypeUrls.Endpoint].Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NonEdsAndRouteClusterReferences_AreIgnored()
    {
        var cluster = new Cluster { Name = "static", Type = Cluster.Types.DiscoveryType.Static,
            EdsClusterConfig = new Cluster.Types.EdsClusterConfig { ServiceName = "ignored" } };
        var route = new RouteConfiguration { Name = "route", VirtualHosts =
        {
            new VirtualHost { Routes = { new Route { Route_ = new RouteAction { Cluster = "unlisted-cluster" } } } },
        } };
        Assert.Empty(ResourceReferences.GetReferences([new(cluster), new(route)]));
    }

    [Fact]
    public void ListenerChains_IncludeRdsDefaultAndScopedListReferences()
    {
        var scoped = new HttpConnectionManager { ScopedRoutes = new ScopedRoutes
        {
            ScopedRouteConfigurationsList = new ScopedRouteConfigurationsList
            {
                ScopedRouteConfigurations = { new ScopedRouteConfiguration { RouteConfigurationName = "scoped" } },
            },
        } };
        var listener = new Listener { Name = "listener", DefaultFilterChain = ResourceFixtures.RdsChain("default"),
            FilterChains = { ResourceFixtures.RdsChain("route"), new FilterChain { Filters = { ResourceFixtures.HcmFilter(scoped) } } } };
        var references = ResourceReferences.GetReferences([new(listener), new(ResourceFixtures.RdsChain("standalone"))]);
        Assert.Equal(new[] { "default", "route", "scoped", "standalone" }, references[ResourceTypeUrls.Route].Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ScopedReferences_IncludeEmptyRouteName()
    {
        var references = ResourceReferences.GetReferences([new(new ScopedRouteConfiguration { Name = "scope" })]);
        Assert.Equal("", Assert.Single(references[ResourceTypeUrls.Route]));
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.ScopedRoute, [new ScopedRouteConfiguration { Name = "scope" }]),
            (ResourceTypeUrls.Route, [new RouteConfiguration()]));
        snapshot.EnsureConsistent();
        Assert.Equal("", Assert.Single(snapshot.GetResources(ResourceTypeUrls.Route)!).Key);
    }

    [Fact]
    public void MalformedAndUnrelatedAny_AreIgnored()
    {
        var malformed = new ListenerFilter { TypedConfig = new Any
        {
            TypeUrl = ResourceTypeUrls.Prefix + HttpConnectionManager.Descriptor.FullName,
            Value = ByteString.CopyFrom([0xff]),
        } };
        var unrelated = new ListenerFilter { TypedConfig = Any.Pack(new Listener { Name = "not-hcm" }) };
        Assert.Null(ResourceReferences.GetHttpConnectionManager(malformed));
        Assert.Null(ResourceReferences.GetHttpConnectionManager(unrelated));
        Assert.Null(ResourceReferences.GetHttpConnectionManager(null));
        Assert.Empty(ResourceReferences.GetReferences([new(new FilterChain { Filters = { malformed, unrelated } })]));
        var manager = ResourceReferences.GetHttpConnectionManager(ResourceFixtures.HcmFilter(new HttpConnectionManager { StatPrefix = "http" }));
        Assert.Equal("http", Assert.IsType<HttpConnectionManager>(manager).StatPrefix);
    }
}
