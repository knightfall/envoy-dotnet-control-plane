using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryFetchCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class FetchEndpointTests
{
    [Fact]
    public async Task WatchOnlyProducer_KeepsStreamingAvailableAndFetchUnsupported()
    {
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "watch-only"));
        await using var host = await StartAsync(services: services => services.AddSingleton<IConfigWatcher>(new WatchOnlyCache(cache)));
        using var stream = Open(host.Channel, ResourceTypeUrls.Cluster, TestContext.Current.CancellationToken);
        await stream.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.True(await Wait(stream.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal("watch-only", stream.ResponseStream.Current.VersionInfo);
        using var fetch = OpenFetch(host.Channel, ResourceTypeUrls.Cluster, Request(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCode.Unknown, (await Assert.ThrowsAsync<RpcException>(() => Wait(fetch.ResponseAsync))).StatusCode);
        await stream.RequestStream.CompleteAsync();
        Assert.False(await Wait(stream.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(null, StatusCode.Unauthenticated)]
    [InlineData("denied", StatusCode.PermissionDenied)]
    [InlineData("allowed", StatusCode.OK)]
    public async Task Authorization_AppliesBeforeUnaryCallbacks(string? credential, StatusCode expected)
    {
        await using var host = await StartAsync(authorize: true);
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "authorized"));
        var headers = new Metadata();
        if (credential is not null) headers.Add("authorization", "Bearer " + credential);
        using var call = OpenFetch(host.Channel, ResourceTypeUrls.Cluster, Request(), TestContext.Current.CancellationToken, headers);
        if (expected == StatusCode.OK)
        {
            Assert.Equal("authorized", (await Wait(call.ResponseAsync)).VersionInfo);
        }
        else
        {
            Assert.Equal(expected, (await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseAsync))).StatusCode);
            Assert.False(host.Callbacks.FetchContexts.Reader.TryRead(out _));
        }
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Theory]
    [InlineData(ResourceTypeUrls.Cluster)]
    [InlineData(ResourceTypeUrls.Endpoint)]
    [InlineData(ResourceTypeUrls.Listener)]
    [InlineData(ResourceTypeUrls.Route)]
    [InlineData(ResourceTypeUrls.ScopedRoute)]
    [InlineData(ResourceTypeUrls.Secret)]
    [InlineData(ResourceTypeUrls.Runtime)]
    [InlineData(ResourceTypeUrls.ExtensionConfig)]
    [InlineData(ResourceTypeUrls.RateLimitConfig)]
    public async Task UnaryMethods_SelectTheirTypeAndPreserveResponseCallbacks(string typeUrl)
    {
        string? selected = null;
        await using var host = await StartAsync(configure: (_, hooks) =>
        {
            hooks.FetchRequestHandler = (_, request) =>
            {
                selected = request.TypeUrl;
                return ValueTask.CompletedTask;
            };
            hooks.FetchResponseHandler = response => response!.Nonce = "response-hook";
        });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(typeUrl, "opaque"));
        using var call = OpenFetch(host.Channel, typeUrl, Request("ignored/type", names: ["a", "missing", "a"]), TestContext.Current.CancellationToken);
        var response = await Wait(call.ResponseAsync);
        Assert.Equal(typeUrl, selected);
        Assert.Equal(typeUrl, response.TypeUrl);
        Assert.Equal("opaque", response.VersionInfo);
        Assert.Equal("response-hook", response.Nonce);
        Assert.Equal(2, response.Resources.Count);
        Assert.All(response.Resources, resource => Assert.Equal(Message(typeUrl, "a").ToByteArray(), resource.Value.ToByteArray()));
        var context = await Wait(host.Callbacks.FetchContexts.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.IsAssignableFrom<ServerCallContext>(context.State);
        Assert.True(context.CancellationToken.CanBeCanceled);
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
        Assert.False(host.Cache.Created.Reader.TryRead(out _));
        Assert.False(host.Callbacks.Opened.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Fact]
    public async Task RateLimitSotw_HasDedicatedStreamingWithoutDedicatedDelta()
    {
        await using var host = await StartAsync();
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.RateLimitConfig, "v1"));
        using var call = Open(host.Channel, ResourceTypeUrls.RateLimitConfig, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal(ResourceTypeUrls.RateLimitConfig, call.ResponseStream.Current.TypeUrl);
        Assert.Equal("v1", call.ResponseStream.Current.VersionInfo);
        Assert.Equal(Message(ResourceTypeUrls.RateLimitConfig, "a").ToByteArray(), Assert.Single(call.ResponseStream.Current.Resources).Value.ToByteArray());
        await call.RequestStream.CompleteAsync();
        Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("missing", StatusCode.Unknown, "Discovery fetch failed.", LogLevel.Debug)]
    [InlineData("unchanged", StatusCode.Unknown, "Discovery fetch failed.", LogLevel.Debug)]
    [InlineData("callback", StatusCode.PermissionDenied, "rejected", null)]
    [InlineData("unsupported", StatusCode.Unknown, "Discovery fetch failed.", LogLevel.Error)]
    [InlineData("timeout", StatusCode.Unknown, "Discovery fetch failed.", LogLevel.Error)]
    [InlineData("unexpected", StatusCode.Unknown, "Discovery fetch failed.", LogLevel.Error)]
    public async Task Failures_RetainProducerAndCallbackGrpcStatusWithPublicDetail(string mode, StatusCode expected, string detail,
        LogLevel? diagnosticLevel)
    {
        using var diagnostics = new DiagnosticLoggerProvider("Envoy.ControlPlane.AspNetCore.FetchGrpc");
        await using var host = await StartAsync(configure: (_, hooks) =>
        {
            if (mode == "callback")
            {
                hooks.FetchRequestHandler = (_, _) => throw new RpcException(new Status(StatusCode.PermissionDenied, "rejected"));
            }
            else if (mode == "timeout")
            {
                hooks.FetchRequestHandler = (_, _) => throw new TaskCanceledException("internal timeout marker");
            }
            else if (mode == "unexpected")
            {
                hooks.FetchResponseHandler = _ => throw new InvalidOperationException("internal response marker");
            }
        }, services: services =>
        {
            services.AddLogging(logging => logging.AddProvider(diagnostics).SetMinimumLevel(LogLevel.Debug));
            services.Configure<GrpcServiceOptions>(options => options.EnableDetailedErrors = true);
            if (mode == "unsupported") services.AddSingleton<IConfigFetcher>(new LinearCache(ResourceTypeUrls.Route));
        });
        if (mode == "unchanged") await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v1"));
        if (mode == "unexpected") await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v2"));
        using var call = OpenFetch(host.Channel, ResourceTypeUrls.Route, Request(version: "v1"), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseAsync));
        Assert.Equal(expected, error.StatusCode);
        Assert.Equal(detail, error.Status.Detail);
        if (diagnosticLevel is { } level)
        {
            Assert.Equal(level, Assert.Single(diagnostics.Entries).Level);
        }
        else
        {
            Assert.Empty(diagnostics.Entries);
        }
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Fact]
    public async Task RequestHook_CanOverrideTheImpliedType()
    {
        await using var host = await StartAsync(configure: (_, hooks) => hooks.FetchRequestHandler = (_, request) =>
        {
            request.TypeUrl = ResourceTypeUrls.Route;
            return ValueTask.CompletedTask;
        });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "redirected"));
        using var call = OpenFetch(host.Channel, ResourceTypeUrls.Cluster, Request(), TestContext.Current.CancellationToken);
        var response = await Wait(call.ResponseAsync);
        Assert.Equal(ResourceTypeUrls.Route, response.TypeUrl);
        Assert.Equal("redirected", response.VersionInfo);
    }

    [Fact]
    public async Task Cancellation_ReachesTheFetchHookAndLeavesTheChannelUsable()
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await StartAsync(configure: (_, hooks) => hooks.FetchRequestHandler = async (context, _) =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            finally
            {
                canceled.TrySetResult();
            }
        });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var call = OpenFetch(host.Channel, ResourceTypeUrls.Route, Request(), stop.Token);
        await Wait(host.Callbacks.FetchContexts.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        stop.Cancel();
        var error = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseAsync));
        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        await Wait(canceled.Task);
        host.Callbacks.FetchRequestHandler = null;
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "after-cancel"));
        using var next = OpenFetch(host.Channel, ResourceTypeUrls.Route, Request(), TestContext.Current.CancellationToken);
        Assert.Equal("after-cancel", (await Wait(next.ResponseAsync)).VersionInfo);
        Assert.Equal(0, host.Cache.Disposals);
    }
}
