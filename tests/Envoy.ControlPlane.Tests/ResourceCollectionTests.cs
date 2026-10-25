using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class ResourceCollectionTests
{
    [Fact]
    public void Indexing_RetainsMessageAndTtl_LastDuplicateWins()
    {
        var first = new Listener { Name = "listener" };
        var replacement = new Listener { Name = "listener", StatPrefix = "replacement" };
        var last = new ResourceEntry(replacement, TimeSpan.Zero);
        var collection = new ResourceCollection("opaque/v2", new ResourceEntry[]
        {
            new(first, TimeSpan.FromSeconds(30)), last,
        });
        Assert.Equal("opaque/v2", collection.Version);
        var item = Assert.Single(collection.Items);
        Assert.Equal("listener", item.Key);
        Assert.Same(last, item.Value);
        Assert.Same(replacement, item.Value.Resource);
        Assert.Equal(TimeSpan.Zero, item.Value.Ttl);
    }

    [Fact]
    public void EmptyCollection_RetainsVersion()
    {
        var collection = new ResourceCollection("", Array.Empty<ResourceEntry>());
        Assert.Equal("", collection.Version);
        Assert.Empty(collection.Items);
    }

    [Fact]
    public void ResourceWithoutTtl_PreservesAbsence()
    {
        var listener = new Listener { Name = "listener" };
        var collection = new ResourceCollection("v1", new[] { listener });
        Assert.Same(listener, collection.Items["listener"].Resource);
        Assert.Null(collection.Items["listener"].Ttl);
    }

    [Fact]
    public void RawIndexing_RetainsLastDuplicateAndUnnamedResources()
    {
        var first = new Listener { Name = "listener" };
        var last = new Listener { Name = "listener", StatPrefix = "replacement" };
        var indexed = ResourceCatalog.IndexResourcesByName([first, last, null]);
        Assert.Equal(new[] { "", "listener" }, indexed.Keys.Order(StringComparer.Ordinal));
        Assert.Same(last, indexed["listener"]);
        Assert.Null(indexed[""]);
    }
}
