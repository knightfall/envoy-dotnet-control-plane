using System.Security.Cryptography;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DeltaDiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class DeltaEndpointTests
{
    [Theory]
    [InlineData("")]
    [InlineData(ResourceTypeUrls.Cluster)]
    [InlineData(ResourceTypeUrls.Endpoint)]
    [InlineData(ResourceTypeUrls.Listener)]
    [InlineData(ResourceTypeUrls.Route)]
    [InlineData(ResourceTypeUrls.ScopedRoute)]
    [InlineData(ResourceTypeUrls.Secret)]
    [InlineData(ResourceTypeUrls.Runtime)]
    [InlineData(ResourceTypeUrls.ExtensionConfig)]
    [InlineData(ResourceTypeUrls.VirtualHost)]
    [InlineData(ResourceTypeUrls.LbEndpoint)]
    public async Task DeltaService_RoundTripsNamedPayloadAndVersionOverHttp2(string serviceType)
    {
        await using var host = await StartAsync();
        string resourceType = serviceType.Length == 0 ? ResourceTypeUrls.Route : serviceType;
        var message = Message(resourceType, "a");
        byte[] bytes = message.ToByteArray();
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(resourceType, "system/v1", message));
        using var call = Open(host.Channel, serviceType, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(serviceType.Length == 0 ? resourceType : "", additions: ["a"]), TestContext.Current.CancellationToken);
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        var response = call.ResponseStream.Current;
        Assert.Equal(resourceType, response.TypeUrl);
        Assert.Equal("system/v1", response.SystemVersionInfo);
        Assert.Equal("1", response.Nonce);
        Assert.Empty(response.RemovedResources);
        var resource = Assert.Single(response.Resources);
        Assert.Equal("a", resource.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), resource.Version);
        Assert.Equal(resourceType, resource.Resource_.TypeUrl);
        Assert.Equal(bytes, resource.Resource_.Value.ToByteArray());
        var context = await Wait(host.Callbacks.Opened.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("HTTP/2", context.GetHttpContext().Request.Protocol);
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("node", (await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).Id);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Fact]
    public async Task DeltaAds_InitialVersionsUpdatesRemovalsNackAndReconnectConverge()
    {
        await using var host = await StartAsync();
        var initial = Snapshot(ResourceTypeUrls.Route, "v1", new RouteConfiguration { Name = "a" });
        initial.ConstructVersionMap();
        string initialHash = initial.GetVersionMap(ResourceTypeUrls.Route)!["a"];
        await host.Cache.Inner.SetSnapshotAsync("node", initial);
        string lastHash;
        using (var call = Open(host.Channel, "", TestContext.Current.CancellationToken))
        {
            var first = Request(ResourceTypeUrls.Route, additions: ["a", "b"]);
            first.InitialResourceVersions.Add("a", initialHash);
            await call.RequestStream.WriteAsync(first, TestContext.Current.CancellationToken);
            await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
            Assert.False(host.Callbacks.DeltaResponses.Reader.TryRead(out _));
            await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v2",
                new RouteConfiguration { Name = "a", IgnorePortInHostMatching = true }, new RouteConfiguration { Name = "b" }));
            Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            var changed = call.ResponseStream.Current;
            Assert.Equal("1", changed.Nonce);
            Assert.Equal("v2", changed.SystemVersionInfo);
            Assert.Equal(["a", "b"], changed.Resources.Select(resource => resource.Name).Order(StringComparer.Ordinal));
            Assert.NotEqual(initialHash, changed.Resources.Single(resource => resource.Name == "a").Version);
            lastHash = changed.Resources.Single(resource => resource.Name == "b").Version;

            var nack = Request(ResourceTypeUrls.Route, "old-unrelated", additions: ["*", "c"]);
            nack.InitialResourceVersions.Add("a", "must-not-reset");
            nack.ErrorDetail = new Google.Rpc.Status { Code = 3, Message = "rejected" };
            await call.RequestStream.WriteAsync(nack, TestContext.Current.CancellationToken);
            await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
            await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v3", new RouteConfiguration { Name = "b" }));
            Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            var removed = call.ResponseStream.Current;
            Assert.Equal("2", removed.Nonce);
            Assert.Equal("v3", removed.SystemVersionInfo);
            Assert.Equal("a", Assert.Single(removed.RemovedResources));
            Assert.Empty(removed.Resources);
            await call.RequestStream.CompleteAsync();
            Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        }
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.Equal("v2", (await Wait(host.Callbacks.DeltaResponses.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).SystemVersionInfo);
        Assert.Equal("v3", (await Wait(host.Callbacks.DeltaResponses.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).SystemVersionInfo);
        Assert.False(host.Callbacks.DeltaResponses.Reader.TryRead(out _));
        using var reconnect = Open(host.Channel, "", TestContext.Current.CancellationToken);
        var known = Request(ResourceTypeUrls.Route, additions: ["b"]);
        known.InitialResourceVersions.Add("b", lastHash);
        await reconnect.RequestStream.WriteAsync(known, TestContext.Current.CancellationToken);
        await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.False(host.Callbacks.DeltaResponses.Reader.TryRead(out _));
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v4",
            new RouteConfiguration { Name = "b", IgnorePortInHostMatching = true }));
        Assert.True(await Wait(reconnect.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        var final = reconnect.ResponseStream.Current;
        Assert.Equal("1", final.Nonce);
        Assert.Equal("v4", final.SystemVersionInfo);
        Assert.NotEqual(lastHash, Assert.Single(final.Resources).Version);
        Assert.Empty(final.RemovedResources);
        await reconnect.RequestStream.CompleteAsync();
        Assert.False(await Wait(reconnect.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal(new long[] { 1, 2 }, host.Callbacks.StreamIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DedicatedDeltaService_RejectsDifferentTypeBeforeRegistration(bool changedByCallback)
    {
        const string serviceType = ResourceTypeUrls.Cluster;
        const string otherType = "type.googleapis.com/example.OtherResource";
        await using var host = await StartAsync(configure: (_, callbacks) =>
        {
            if (changedByCallback)
            {
                callbacks.DeltaRequestHandler = (request, _) =>
                {
                    Assert.Empty(request.TypeUrl);
                    request.TypeUrl = otherType;
                    return ValueTask.CompletedTask;
                };
            }
        });
        using var call = Open(host.Channel, serviceType, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(changedByCallback ? "" : otherType), TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
        Assert.Equal(changedByCallback, host.Callbacks.DeltaRequested.Reader.TryRead(out _));
        Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
    }

    [Fact]
    public async Task DeltaSubscriptionLimit_CountsRetainedNamesAndPrefixesAcrossTypes()
    {
        const string firstType = "example/First";
        const string secondType = "example/Second";
        await using var host = await StartAsync(deltaOptions: new DeltaOptions { MaxSubscriptionEntries = 3 });
        var token = TestContext.Current.CancellationToken;
        using var call = Open(host.Channel, "", token);
        foreach (var request in new[]
        {
            Request(firstType, additions: ["alpha", "collection/*", "alpha"]),
            Request(secondType, additions: ["beta"]),
            Request(firstType, additions: ["replacement"], removals: ["collection/*"]),
        })
        {
            await call.RequestStream.WriteAsync(request, token);
            await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(token).AsTask());
        }
        await call.RequestStream.WriteAsync(Request(secondType, additions: ["other/*"]), token);
        var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(token)));
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);
        Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Fact]
    public async Task DedicatedDeltaSubscriptionLimit_AppliesCharacterBudgetAfterCallback()
    {
        await using var host = await StartAsync(configure: (_, callbacks) => callbacks.DeltaRequestHandler = (request, _) =>
            {
                if (request.ResourceNamesSubscribe.Count == 0)
                {
                    request.ResourceNamesSubscribe.Add("four/*");
                }
                return ValueTask.CompletedTask;
            }, deltaOptions: new DeltaOptions { MaxSubscriptionCharacters = 6 });
        var token = TestContext.Current.CancellationToken;
        using var call = Open(host.Channel, ResourceTypeUrls.Cluster, token);
        await call.RequestStream.WriteAsync(Request(additions: ["a"]), token);
        await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(token).AsTask());
        await call.RequestStream.WriteAsync(Request(), token);
        await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(token).AsTask());
        await call.RequestStream.WriteAsync(Request(additions: ["b"]), token);
        var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(token)));
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);
        Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Fact]
    public async Task DeltaSubscriptionLimit_CountsPendingWildcardResets()
    {
        await using var host = await StartAsync(deltaOptions: new DeltaOptions { MaxSubscriptionEntries = 1 });
        var token = TestContext.Current.CancellationToken;
        using var call = Open(host.Channel, ResourceTypeUrls.Cluster, token);
        await call.RequestStream.WriteAsync(Request(nonce: "watch", additions: ["*", "first"], removals: ["first"]), token);
        await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(token).AsTask());
        await call.RequestStream.WriteAsync(Request(nonce: "watch", additions: ["second"], removals: ["second"]), token);
        var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(token)));
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);
        Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeltaSubscriptionLimit_CountsVersionMapsAcrossPendingWatches(bool reuseType)
    {
        const string firstType = "example/First";
        const string secondType = "example/Second";
        await using var host = await StartAsync(deltaOptions: new DeltaOptions { MaxSubscriptionEntries = 2 });
        var token = TestContext.Current.CancellationToken;
        using var call = Open(host.Channel, "", token);
        foreach (string type in new[] { firstType, secondType })
        {
            var request = Request(type);
            request.InitialResourceVersions.Add("known", "v1");
            await call.RequestStream.WriteAsync(request, token);
            await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(token).AsTask());
        }
        var next = Request(reuseType ? firstType : "example/Third");
        next.InitialResourceVersions.Add("known", "v2");
        await call.RequestStream.WriteAsync(next, token);
        var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(token)));
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);
        Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Theory]
    [InlineData("😀", true)]
    [InlineData("😀x", false)]
    public async Task DedicatedDeltaSubscriptionLimit_CountsVersionKeyAndValueAfterCallback(string version, bool accepted)
    {
        await using var host = await StartAsync(configure: (_, callbacks) => callbacks.DeltaRequestHandler = (request, _) =>
            {
                request.InitialResourceVersions.Add("ab", version);
                return ValueTask.CompletedTask;
            }, deltaOptions: new DeltaOptions { MaxSubscriptionCharacters = 4 });
        var token = TestContext.Current.CancellationToken;
        using var call = Open(host.Channel, ResourceTypeUrls.Cluster, token);
        await call.RequestStream.WriteAsync(Request(), token);
        if (accepted)
        {
            var registered = await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(token).AsTask());
            Assert.Equal(version, registered.InitialResourceVersions["ab"]);
            await call.RequestStream.CompleteAsync();
            Assert.False(await Wait(call.ResponseStream.MoveNext(token)));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(token)));
            Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);
            Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
        }
    }

    [Fact]
    public async Task DeltaSubscriptionLimit_ReleasesVersionBudgetAfterDelivery()
    {
        await using var host = await StartAsync(deltaOptions: new DeltaOptions { MaxSubscriptionEntries = 1 });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v1",
            new RouteConfiguration { Name = "a" }, new RouteConfiguration { Name = "b" }));
        var token = TestContext.Current.CancellationToken;
        using var call = Open(host.Channel, "", token);
        var route = Request(ResourceTypeUrls.Route);
        route.InitialResourceVersions.Add("a", "old");
        await call.RequestStream.WriteAsync(route, token);
        Assert.True(await Wait(call.ResponseStream.MoveNext(token)));
        Assert.Equal(["a", "b"], call.ResponseStream.Current.Resources.Select(resource => resource.Name).Order(StringComparer.Ordinal));

        var cluster = Request(ResourceTypeUrls.Cluster);
        cluster.InitialResourceVersions.Add("retired", "old");
        await call.RequestStream.WriteAsync(cluster, token);
        Assert.True(await Wait(call.ResponseStream.MoveNext(token)));
        Assert.Equal("retired", Assert.Single(call.ResponseStream.Current.RemovedResources));
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(token)));
    }

    [Fact]
    public async Task DeltaAdsTypeLimit_AcceptsCustomTypesAndExistingSubscriptions()
    {
        await using var host = await StartAsync(deltaOptions: new DeltaOptions { MaxAdsResourceTypes = 2 });
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        foreach (string type in new[] { "example/First", "example/Second", "example/First" })
        {
            await call.RequestStream.WriteAsync(Request(type), TestContext.Current.CancellationToken);
            var registered = await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(type, registered.TypeUrl);
        }
        await call.RequestStream.WriteAsync(Request("example/Third"), TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);
        Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredDeltaOptions_ControlLegacyWildcard(bool disabled)
    {
        await using var host = await StartAsync(deltaOptions: new DeltaOptions { DisableLegacyWildcard = disabled });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v", Message(ResourceTypeUrls.Route, "a")));
        using var call = Open(host.Channel, ResourceTypeUrls.Route, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
        await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        if (disabled)
        {
            Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
            Assert.False(host.Callbacks.DeltaResponses.Reader.TryRead(out _));
        }
        else
        {
            Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            Assert.Equal("a", Assert.Single(call.ResponseStream.Current.Resources).Name);
        }
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
    }
}
