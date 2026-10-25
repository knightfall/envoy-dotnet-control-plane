using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotResponseTests
{
    [Fact]
    public async Task Materialization_IsLazyAndCachedWithRequestAndVersionRetained()
    {
        var cache = new SnapshotCache();
        var message = new CountingResource("listener");
        var request = Request(ResourceTypeUrls.Listener);
        using var watch = cache.CreateWatch(request, Subscription(wildcard: true));
        cache.SetSnapshot("node", ResourceFixtures.Snapshot("opaque", (ResourceTypeUrls.Listener, [message])));
        var response = await watch.Completion;
        Assert.Equal(0, message.Writes);
        Assert.Same(request, response.Request);
        var discovery = response.GetDiscoveryResponse();
        Assert.Equal(1, message.Writes);
        Assert.Equal("opaque", discovery.VersionInfo);
        Assert.Equal(ResourceTypeUrls.Listener, discovery.TypeUrl);
        Assert.Equal("", discovery.Nonce);
        var payload = Assert.Single(discovery.Resources);
        Assert.Equal(ResourceTypeUrls.Listener, payload.TypeUrl);
        Assert.Equal("listener", StringValue.Parser.ParseFrom(payload.Value).Value);
        Assert.Same(discovery, response.GetDiscoveryResponse());
        Assert.Equal(1, message.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(30)]
    public async Task NormalResponse_PreservesTtlPresenceAndUnknownPayloadFields(int? ttlSeconds)
    {
        var original = new Listener { Name = "listener" };
        byte[] wire = [.. original.ToByteArray(), 0xa0, 0x1f, 0x07];
        var message = Listener.Parser.ParseFrom(wire);
        var snapshot = new Snapshot("v1", new Dictionary<string, IEnumerable<ResourceEntry>>
        {
            [ResourceTypeUrls.Listener] = [new(message, ttlSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null)],
        });
        var cache = new SnapshotCache();
        cache.SetSnapshot("node", snapshot);
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var response = await watch.Completion;
        var payload = Assert.Single(response.GetDiscoveryResponse().Resources);
        if (ttlSeconds is { } expected)
        {
            var wrapper = payload.Unpack<DiscoveryResource>();
            Assert.Equal("listener", wrapper.Name);
            Assert.Equal(TimeSpan.FromSeconds(expected), wrapper.Ttl.ToTimeSpan());
            Assert.NotNull(wrapper.Resource_);
            payload = wrapper.Resource_;
        }
        Assert.Equal(ResourceTypeUrls.Listener, payload.TypeUrl);
        Assert.Equal(wire, payload.Value.ToByteArray());
        Assert.Equal("v1", response.ReturnedResources["listener"]);
    }

    [Fact]
    public async Task ReplacementPublication_DoesNotChangePreviouslyDeliveredResources()
    {
        var cache = new SnapshotCache();
        cache.SetSnapshot("node", ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [new Listener { Name = "listener", StatPrefix = "old" }])));
        using var first = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var retained = await first.Completion;
        cache.SetSnapshot("node", ResourceFixtures.Snapshot("v2", (ResourceTypeUrls.Listener, [new Listener { Name = "listener", StatPrefix = "new" }])));
        using var second = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var replacement = await second.Completion;
        Assert.Equal("old", Assert.Single(retained.GetDiscoveryResponse().Resources).Unpack<Listener>().StatPrefix);
        Assert.Equal("new", Assert.Single(replacement.GetDiscoveryResponse().Resources).Unpack<Listener>().StatPrefix);
        Assert.Equal("v1", retained.ResponseVersion);
        Assert.Equal("v2", replacement.ResponseVersion);
    }

    [Fact]
    public async Task SerializationFailure_StaysLazyAndVisibleAfterWatchDelivery()
    {
        var failure = new InvalidOperationException("cannot serialize resource");
        var resource = new ThrowingResource(failure);
        var cache = new SnapshotCache();
        cache.SetSnapshot("node", ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [resource])));
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var response = await watch.Completion;
        Assert.Equal(0, resource.Attempts);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(response.GetDiscoveryResponse));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(response.GetDiscoveryResponse));
        Assert.Equal(1, resource.Attempts);
        Assert.Equal("v1", response.ResponseVersion);
    }

}
