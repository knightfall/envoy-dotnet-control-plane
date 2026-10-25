using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Google.Protobuf;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public void PartialSnapshot_DistinguishesAbsentAndExplicitEmptyGroups()
    {
        var snapshot = ResourceFixtures.Snapshot("opaque/version", (ResourceTypeUrls.Route, []));
        Assert.Equal("opaque/version", snapshot.GetVersion(ResourceTypeUrls.Route));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyDictionary<string, IMessage?>>(snapshot.GetResources(ResourceTypeUrls.Route)));
        Assert.Equal("", snapshot.GetVersion(ResourceTypeUrls.Endpoint));
        Assert.Null(snapshot.GetResources(ResourceTypeUrls.Endpoint));
        Assert.Null(snapshot.GetResourcesAndTtl(ResourceTypeUrls.Endpoint));
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Route));
        snapshot.EnsureConsistent();
    }

    [Fact]
    public void UnknownType_ConstructionRejectsAndQueriesReturnAbsent()
    {
        var snapshot = ResourceFixtures.Snapshot("v1");
        Assert.Null(snapshot.GetResources("custom/type"));
        Assert.Equal("", snapshot.GetVersion("custom/type"));
        var error = Assert.Throws<ArgumentException>(() => ResourceFixtures.Snapshot("v1", ("custom/type", [])));
        Assert.Contains("Unknown resource type: custom/type", error.Message);
    }

    [Fact]
    public void ResourceGroups_RetainIndependentOpaqueVersions()
    {
        var clusters = new ResourceCollection("clusters/v9", new IMessage[] { new Cluster { Name = "backend" } });
        var listeners = new ResourceCollection("listeners/v2", new IMessage[] { new Listener { Name = "listener" } });
        var snapshot = new Snapshot(new Dictionary<string, ResourceCollection>
        {
            [ResourceTypeUrls.Cluster] = clusters,
            [ResourceTypeUrls.Listener] = listeners,
        });
        Assert.Equal("clusters/v9", snapshot.GetVersion(ResourceTypeUrls.Cluster));
        Assert.Equal("listeners/v2", snapshot.GetVersion(ResourceTypeUrls.Listener));
        Assert.Same(clusters.Items, snapshot.GetResourcesAndTtl(ResourceTypeUrls.Cluster));
        Assert.Equal("", snapshot.GetVersion(ResourceTypeUrls.Route));
    }

    [Fact]
    public void Messages_AreRetained_AndReplacementSnapshotIsIndependent()
    {
        var original = new Listener { Name = "listener", StatPrefix = "old" };
        var replacement = new Listener { Name = "listener", StatPrefix = "new" };
        var first = ResourceFixtures.Snapshot("v9", (ResourceTypeUrls.Listener, [original]));
        var second = ResourceFixtures.Snapshot("v2", (ResourceTypeUrls.Listener, [replacement]));
        Assert.Same(original, first.GetResources(ResourceTypeUrls.Listener)!["listener"]);
        Assert.Same(replacement, second.GetResources(ResourceTypeUrls.Listener)!["listener"]);
        Assert.Equal("v9", first.GetVersion(ResourceTypeUrls.Listener));
        Assert.Equal("v2", second.GetVersion(ResourceTypeUrls.Listener));
        first.ConstructVersionMap();
        second.ConstructVersionMap();
        Assert.NotEqual(first.GetVersionMap(ResourceTypeUrls.Listener)!["listener"], second.GetVersionMap(ResourceTypeUrls.Listener)!["listener"]);
        Assert.Equal("old", original.StatPrefix);
    }

    [Fact]
    public void VersionMap_IsExplicitAndCached()
    {
        var message = new CountingResource("resource");
        var snapshot = ResourceFixtures.Snapshot("opaque", (ResourceTypeUrls.Listener, [message]));
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Listener));
        Assert.Equal(0, message.Writes);
        Assert.Same(message, snapshot.GetResources(ResourceTypeUrls.Listener)!["resource"]);
        Assert.Equal(0, message.Writes);
        snapshot.ConstructVersionMap();
        var versions = snapshot.GetVersionMap(ResourceTypeUrls.Listener)!;
        Assert.Equal("resource", Assert.Single(versions).Key);
        Assert.Equal(64, versions["resource"].Length);
        Assert.Equal(1, message.Writes);
        snapshot.ConstructVersionMap();
        Assert.Same(versions, snapshot.GetVersionMap(ResourceTypeUrls.Listener));
        Assert.Equal(1, message.Writes);
        Assert.Empty(snapshot.GetVersionMap(ResourceTypeUrls.Endpoint)!);
        Assert.Null(snapshot.GetVersionMap("custom/type"));
    }

    [Fact]
    public void VersionMap_SamePayload_IgnoresCallerVersionAndTtl()
    {
        var first = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]));
        var second = new Snapshot("v99", new Dictionary<string, IEnumerable<ResourceEntry>>
        {
            [ResourceTypeUrls.Listener] = [new(new Listener { Name = "listener" }, TimeSpan.FromSeconds(30))],
        });
        first.ConstructVersionMap();
        second.ConstructVersionMap();
        Assert.Equal(first.GetVersionMap(ResourceTypeUrls.Listener)!["listener"], second.GetVersionMap(ResourceTypeUrls.Listener)!["listener"]);
        Assert.NotEqual(first.GetVersion(ResourceTypeUrls.Listener), second.GetVersion(ResourceTypeUrls.Listener));
        Assert.Null(first.GetResourcesAndTtl(ResourceTypeUrls.Listener)!["listener"].Ttl);
        Assert.Equal(TimeSpan.FromSeconds(30), second.GetResourcesAndTtl(ResourceTypeUrls.Listener)!["listener"].Ttl);
    }

    [Fact]
    public void KnownType_DoesNotRequireMatchingMessageClass()
    {
        var resource = new Google.Protobuf.WellKnownTypes.StringValue { Value = "opaque" };
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Cluster, [resource]));
        Assert.Same(resource, snapshot.GetResources(ResourceTypeUrls.Cluster)![""]);
        snapshot.EnsureConsistent();
    }

    [Fact]
    public void ResourcesWithoutTtl_AreNewIndexes_RetainingMessages()
    {
        var listener = new Listener { Name = "listener" };
        var snapshot = new Snapshot("v1", new Dictionary<string, IEnumerable<ResourceEntry>>
        {
            [ResourceTypeUrls.Listener] = [new(listener, TimeSpan.FromSeconds(30))],
        });
        var first = snapshot.GetResources(ResourceTypeUrls.Listener)!;
        var second = snapshot.GetResources(ResourceTypeUrls.Listener)!;
        Assert.NotSame(first, second);
        Assert.Same(listener, first["listener"]);
        Assert.Equal(TimeSpan.FromSeconds(30), snapshot.GetResourcesAndTtl(ResourceTypeUrls.Listener)!["listener"].Ttl);
        Assert.IsType<Dictionary<string, IMessage?>>(first).Clear();
        Assert.Same(listener, second["listener"]);
        Assert.Same(listener, snapshot.GetResources(ResourceTypeUrls.Listener)!["listener"]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-name")]
    public void Consistency_ChecksExactEndpointSets(string scenario)
    {
        var endpoint = new ClusterLoadAssignment { ClusterName = scenario == "wrong-name" ? "wrong" : "backend" };
        IMessage[] endpoints = scenario switch
        {
            "missing" => [],
            "extra" => [endpoint, new ClusterLoadAssignment { ClusterName = "extra" }],
            _ => [endpoint],
        };
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Cluster, [ResourceFixtures.EdsCluster("backend")]),
            (ResourceTypeUrls.Endpoint, endpoints));
        Assert.Contains(ResourceTypeUrls.Endpoint, Assert.Throws<InvalidOperationException>(snapshot.EnsureConsistent).Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-name")]
    public void Consistency_ChecksExactRouteSets(string scenario)
    {
        var listener = new Listener { Name = "listener", FilterChains = { ResourceFixtures.RdsChain("route") } };
        IMessage[] routes = scenario switch
        {
            "missing" => [],
            "extra" => [new RouteConfiguration { Name = "route" }, new RouteConfiguration { Name = "extra" }],
            _ => [new RouteConfiguration { Name = "wrong" }],
        };
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [listener]), (ResourceTypeUrls.Route, routes));
        Assert.Contains(ResourceTypeUrls.Route, Assert.Throws<InvalidOperationException>(snapshot.EnsureConsistent).Message);
    }

    [Fact]
    public void Constructor_DoesNotCheckConsistencyOrEvaluateRules()
    {
        var invalidCluster = new Cluster { Type = Cluster.Types.DiscoveryType.Eds };
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Cluster, [invalidCluster]));
        Assert.Same(invalidCluster, snapshot.GetResources(ResourceTypeUrls.Cluster)![""]);
        Assert.Throws<InvalidOperationException>(snapshot.EnsureConsistent);
    }
}
