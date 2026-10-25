using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class HostingLifecycleTests
{
    [Theory]
    [InlineData("missing-type", StatusCode.InvalidArgument)]
    [InlineData("closed-watch", StatusCode.Unavailable)]
    [InlineData("callback", StatusCode.PermissionDenied)]
    public async Task CoreRejection_MapsStatusAndClosesTheCall(string failure, StatusCode expected)
    {
        await using var host = await StartAsync(configure: (cache, callbacks) =>
        {
            cache.CloseResponsesOnCreate = failure == "closed-watch";
            if (failure == "callback")
            {
                callbacks.RequestFailure = new RpcException(new Status(StatusCode.PermissionDenied, "callback rejection"));
            }
        });
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        var rejected = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(Request(failure == "missing-type" ? "" : ResourceTypeUrls.Cluster), TestContext.Current.CancellationToken);
            await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        });
        Assert.Equal(expected, rejected.StatusCode);
        Assert.Equal("node", (await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).Id);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")?.NumWatches ?? 0);
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Fact]
    public async Task ClientCancellation_ReleasesWatchAndLeavesCallerChannelUsable()
    {
        await using var host = await StartAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using (var call = Open(host.Channel, "", cancellation.Token))
        {
            await call.RequestStream.WriteAsync(Request(ResourceTypeUrls.Cluster), cancellation.Token);
            await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            Assert.Equal(StatusCode.Cancelled, canceled.StatusCode);
            await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        }
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "after-cancel"));
        using var next = Open(host.Channel, ResourceTypeUrls.Cluster, TestContext.Current.CancellationToken);
        await next.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.True(await Wait(next.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("after-cancel", next.ResponseStream.Current.VersionInfo);
        Assert.Equal(new long[] { 1, 2 }, host.Callbacks.StreamIds);
        await next.RequestStream.CompleteAsync();
        Assert.False(await Wait(next.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task HostShutdown_ClosesPendingCallWithoutDisposingCallerCache()
    {
        await using var host = await StartAsync();
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(ResourceTypeUrls.Cluster), TestContext.Current.CancellationToken);
        await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        var stopping = host.Application.StopAsync(TestContext.Current.CancellationToken);
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        await Wait(stopping);
        await host.DisposeAsync();
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Theory]
    [InlineData(null, StatusCode.Unauthenticated)]
    [InlineData("denied", StatusCode.PermissionDenied)]
    [InlineData("allowed", StatusCode.OK)]
    public async Task StandardHostAuthorization_AppliesToMappedGrpcGroup(string? credential, StatusCode expected)
    {
        await using var host = await StartAsync(authorize: true);
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "v1"));
        var headers = new Metadata();
        if (credential is not null)
        {
            headers.Add("authorization", "Bearer " + credential);
        }
        using var call = Open(host.Channel, ResourceTypeUrls.Cluster, TestContext.Current.CancellationToken, headers);
        if (expected != StatusCode.OK)
        {
            var rejected = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
                await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
            });
            Assert.Equal(expected, rejected.StatusCode);
            Assert.False(host.Cache.Created.Reader.TryRead(out _));
            Assert.False(host.Callbacks.Opened.Reader.TryRead(out _));
        }
        else
        {
            await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
            Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            Assert.Equal("v1", call.ResponseStream.Current.VersionInfo);
            var context = await Wait(host.Callbacks.Opened.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.True(context.GetHttpContext().User.Identity!.IsAuthenticated);
            Assert.Equal("node", context.GetHttpContext().User.FindFirst("xds-node")!.Value);
            await call.RequestStream.CompleteAsync();
            Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        }
    }

    [Fact]
    public async Task DeferredMethods_RetainGeneratedUnimplementedStatus()
    {
        await using var host = await StartAsync();
        var endpoint = new Envoy.Service.Endpoint.V3.EndpointDiscoveryService.EndpointDiscoveryServiceClient(host.Channel);
        var fetch = await Assert.ThrowsAsync<RpcException>(async () =>
            await endpoint.FetchEndpointsAsync(Request(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unimplemented, fetch.StatusCode);
        var ads = new Envoy.Service.Discovery.V3.AggregatedDiscoveryService.AggregatedDiscoveryServiceClient(host.Channel);
        using var delta = ads.DeltaAggregatedResources(cancellationToken: TestContext.Current.CancellationToken);
        var unsupported = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await delta.RequestStream.WriteAsync(new Envoy.Service.Discovery.V3.DeltaDiscoveryRequest(), TestContext.Current.CancellationToken);
            await Wait(delta.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        });
        Assert.Equal(StatusCode.Unimplemented, unsupported.StatusCode);
        Assert.False(host.Cache.Created.Reader.TryRead(out _));
        Assert.False(host.Callbacks.Opened.Reader.TryRead(out _));
    }
}
