using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Clients;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class SotwAdsClientIntegrationTests
{
    [Fact]
    public async Task SnapshotUpdates_AckAndNackRoundTrip_AndDisposalLeavesTheExternalChannelUsable()
    {
        await using var host = await StartAsync();
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v1", "first"));
        var invoker = host.Channel.CreateCallInvoker();
        using var client = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        await client.InitializeAsync(invoker, new CallOptions(cancellationToken: TestContext.Current.CancellationToken));
        var initial = await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Empty(initial.VersionInfo);
        Assert.Empty(initial.ResponseNonce);
        Assert.Equal(ResourceTypeUrls.Route, initial.TypeUrl);
        Assert.Equal("first", Assert.Single(await Wait(client.FetchAsync(TestContext.Current.CancellationToken))).Unpack<RouteConfiguration>().Name);
        await client.AckAsync(TestContext.Current.CancellationToken);
        var accepted = await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("v1", accepted.VersionInfo);
        Assert.Equal("1", accepted.ResponseNonce);
        Assert.Null(accepted.ErrorDetail);
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v2", "second"));
        Assert.Equal("second", Assert.Single(await Wait(client.FetchAsync(TestContext.Current.CancellationToken))).Unpack<RouteConfiguration>().Name);
        await client.NackAsync("application rejected the update", TestContext.Current.CancellationToken);
        var rejected = await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("v1", rejected.VersionInfo);
        Assert.Equal("2", rejected.ResponseNonce);
        Assert.Equal("application rejected the update", rejected.ErrorDetail.Message);
        Assert.Equal(0, rejected.ErrorDetail.Code);
        client.Dispose();
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        using var next = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        await next.InitializeAsync(invoker, new CallOptions(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("second", Assert.Single(await Wait(next.FetchAsync(TestContext.Current.CancellationToken))).Unpack<RouteConfiguration>().Name);
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Fact]
    public async Task ServerUnavailable_IsExposedWithoutAutomaticReconnection()
    {
        await using var host = await StartAsync(configure: (_, hooks) =>
            hooks.RequestFailure = new RpcException(new Status(StatusCode.Unavailable, "server unavailable")));
        using var client = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        var error = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.InitializeAsync(host.Channel.CreateCallInvoker(), new CallOptions(cancellationToken: TestContext.Current.CancellationToken));
            await Wait(client.FetchAsync(TestContext.Current.CancellationToken));
        });
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.True(SotwAdsClient.IsConnectionError(error));
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Single(host.Callbacks.StreamIds);
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }
}
