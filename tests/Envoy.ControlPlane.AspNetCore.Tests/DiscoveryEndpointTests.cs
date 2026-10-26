using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class DiscoveryEndpointTests
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
    public async Task PreviewService_RoundTripsSnapshotOverHttp2AndClosesItsWatch(string serviceType)
    {
        await using var host = await StartAsync();
        string resourceType = serviceType.Length == 0 ? ResourceTypeUrls.Route : serviceType;
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(resourceType, "v1"));
        using var call = Open(host.Channel, serviceType, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(serviceType.Length == 0 ? resourceType : "", names: ["a"]), TestContext.Current.CancellationToken);
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        var response = call.ResponseStream.Current;
        Assert.Equal(resourceType, response.TypeUrl);
        Assert.Equal("v1", response.VersionInfo);
        Assert.Equal("1", response.Nonce);
        Assert.Equal(Message(resourceType, "a").ToByteArray(), Assert.Single(response.Resources).Value.ToByteArray());
        var context = await Wait(host.Callbacks.Opened.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("HTTP/2", context.GetHttpContext().Request.Protocol);
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("node", (await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).Id);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ads_AckUpdateAndNackUseTheSameCoreStateOverHttp2(bool ordered)
    {
        await using var host = await StartAsync(new SotwOptions { OrderedAds = ordered });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "v1"));
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(ResourceTypeUrls.Cluster, names: ["a"]), TestContext.Current.CancellationToken);
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("v1", call.ResponseStream.Current.VersionInfo);
        await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        await call.RequestStream.WriteAsync(Request(ResourceTypeUrls.Cluster, "v1", "1", "a"), TestContext.Current.CancellationToken);
        await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "v2"));
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("v2", call.ResponseStream.Current.VersionInfo);
        Assert.Equal("2", call.ResponseStream.Current.Nonce);
        var nack = Request(ResourceTypeUrls.Cluster, "v1", "2", "a");
        nack.ErrorDetail = new Google.Rpc.Status { Code = 3, Message = "rejected" };
        await call.RequestStream.WriteAsync(nack, TestContext.Current.CancellationToken);
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("v2", call.ResponseStream.Current.VersionInfo);
        Assert.Equal("3", call.ResponseStream.Current.Nonce);
        Assert.Equal("v2", host.Cache.Inner.GetSnapshot("node").GetVersion(ResourceTypeUrls.Cluster));
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredOptions_ControlLegacyWildcardBehavior(bool disabled)
    {
        await using var host = await StartAsync(new SotwOptions { DisableLegacyWildcard = disabled });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v1"));
        using var call = Open(host.Channel, ResourceTypeUrls.Route, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal(disabled ? 0 : 1, call.ResponseStream.Current.Resources.Count);
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task EndpointInventory_ContainsSotwAndDeltaContractsWithoutRateLimitHosting()
    {
        await using var host = await StartAsync();
        var methods = ((IEndpointRouteBuilder)host.Application).DataSources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<GrpcMethodMetadata>()).OfType<GrpcMethodMetadata>()
            .Select(metadata => metadata.Method).ToArray();
        Assert.Equal(28, methods.Length);
        Assert.Equal(11, methods.Select(method => method.ServiceName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { "StreamAggregatedResources", "StreamClusters", "StreamEndpoints", "StreamExtensionConfigs", "StreamListeners",
                "StreamRoutes", "StreamRuntime", "StreamScopedRoutes", "StreamSecrets" },
            methods.Where(method => method.Type == MethodType.DuplexStreaming && !method.Name.StartsWith("Delta", StringComparison.Ordinal))
                .Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "DeltaAggregatedResources", "DeltaClusters", "DeltaEndpoints", "DeltaExtensionConfigs", "DeltaListeners",
                "DeltaLocalityEndpoints", "DeltaRoutes", "DeltaRuntime", "DeltaScopedRoutes", "DeltaSecrets", "DeltaVirtualHosts" },
            methods.Where(method => method.Name.StartsWith("Delta", StringComparison.Ordinal))
                .Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(methods, method => method.Name.Contains("RlsConfig", StringComparison.Ordinal));
    }
}
