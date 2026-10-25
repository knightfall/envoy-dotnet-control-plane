using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Example;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class PreviewGraphTests
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        [ResourceTypeUrls.Cluster] = "service",
        [ResourceTypeUrls.Endpoint] = "service",
        [ResourceTypeUrls.Route] = "routes",
        [ResourceTypeUrls.Listener] = "listener",
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExampleGraph_UpdatesAllFourFamiliesThroughAds(bool ordered)
    {
        await using var host = await StartAsync(new SotwOptions { OrderedAds = ordered });
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        foreach (var (type, name) in Names)
        {
            await call.RequestStream.WriteAsync(Request(type, names: [name]), TestContext.Current.CancellationToken);
            await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        }
        Assert.Equal(4, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        await host.Cache.Inner.SetSnapshotAsync("node", ExampleResources.CreateSnapshot("v1", 8080, 10000));
        var first = await ReadGraphAsync(call, "v1");
        Assert.Equal(new[] { "1", "2", "3", "4" }, first.Values.Select(response => response.Nonce).Order(StringComparer.Ordinal));
        var listener = Assert.Single(first[ResourceTypeUrls.Listener].Resources).Unpack<Listener>();
        var manager = ResourceReferences.GetHttpConnectionManager(Assert.Single(Assert.Single(listener.FilterChains).Filters));
        Assert.Equal("routes", manager!.Rds.RouteConfigName);
        Assert.Equal(8080U, BackendPort(first[ResourceTypeUrls.Endpoint]));
        foreach (var (type, name) in Names)
        {
            await call.RequestStream.WriteAsync(Request(type, "v1", first[type].Nonce, name), TestContext.Current.CancellationToken);
            await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        }
        Assert.Equal(4, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        await host.Cache.Inner.SetSnapshotAsync("node", ExampleResources.CreateSnapshot("v2", 8081, 10000));
        var second = await ReadGraphAsync(call, "v2");
        Assert.Equal(new[] { "5", "6", "7", "8" }, second.Values.Select(response => response.Nonce).Order(StringComparer.Ordinal));
        Assert.Equal(8081U, BackendPort(second[ResourceTypeUrls.Endpoint]));
        Assert.Equal(8080U, BackendPort(first[ResourceTypeUrls.Endpoint]));
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
    }

    private static async Task<Dictionary<string, DiscoveryResponse>> ReadGraphAsync(AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse> call, string version)
    {
        var received = new Dictionary<string, DiscoveryResponse>(StringComparer.Ordinal);
        for (int index = 0; index < Names.Count; index++)
        {
            Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            var response = call.ResponseStream.Current;
            Assert.Contains(response.TypeUrl, Names.Keys);
            Assert.Equal(version, response.VersionInfo);
            Assert.Equal(response.TypeUrl, Assert.Single(response.Resources).TypeUrl);
            Assert.True(received.TryAdd(response.TypeUrl, response));
        }
        return received;
    }

    private static uint BackendPort(DiscoveryResponse response)
    {
        var endpoints = Assert.Single(response.Resources).Unpack<ClusterLoadAssignment>();
        Assert.Equal("service", endpoints.ClusterName);
        return Assert.Single(Assert.Single(endpoints.Endpoints).LbEndpoints).Endpoint.Address.SocketAddress.PortValue;
    }
}
