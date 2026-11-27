using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DeltaDiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class DeltaHostingLifecycleTests
{
    [Theory]
    [InlineData("unexpected", StatusCode.Internal, "Delta discovery stream failed.")]
    [InlineData("timeout", StatusCode.Internal, "Delta discovery stream failed.")]
    [InlineData("status", StatusCode.PermissionDenied, "application status")]
    public async Task DeltaResponseFailure_ReturnsOnlyItsPublicStatus(string mode, StatusCode expected, string detail)
    {
        const string diagnostic = "internal diagnostic marker";
        Exception failure = mode switch
        {
            "status" => new RpcException(new Status(expected, detail)),
            "timeout" => new OperationCanceledException(diagnostic),
            _ => new InvalidOperationException(diagnostic),
        };
        using var diagnostics = new DiagnosticLoggerProvider("Envoy.ControlPlane.AspNetCore.DeltaGrpcStream");
        await using var host = await StartAsync(configure: (_, callbacks) =>
        {
            callbacks.DeltaResponseHandler = (_, _) => ValueTask.FromException(failure);
            if (mode == "status")
            {
                callbacks.CloseFailure = new InvalidOperationException("cleanup diagnostic marker");
            }
        }, services: services =>
        {
            services.AddLogging(logging => logging.AddProvider(diagnostics));
            services.Configure<GrpcServiceOptions>(options => options.EnableDetailedErrors = true);
        });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "v1"));
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(Request(ResourceTypeUrls.Cluster), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        Assert.Equal(expected, error.StatusCode);
        Assert.Equal(detail, error.Status.Detail);
        var entry = Assert.Single(diagnostics.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.StartsWith("Delta discovery stream failed for type ", entry.Message, StringComparison.Ordinal);
        if (mode == "status")
        {
            var aggregate = Assert.IsType<AggregateException>(entry.Error);
            Assert.Same(failure, aggregate.InnerException);
            Assert.Equal("cleanup diagnostic marker", aggregate.InnerExceptions[1].Message);
        }
    }

    [Theory]
    [InlineData("missing-type", StatusCode.InvalidArgument)]
    [InlineData("closed-watch", StatusCode.Unavailable)]
    [InlineData("callback", StatusCode.PermissionDenied)]
    public async Task DeltaRejection_MapsStatusAndClosesTheCall(string failure, StatusCode expected)
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
        var node = await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(failure == "callback" ? "" : "node", node.Id);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")?.NumDeltaWatches ?? 0);
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Theory]
    [InlineData(null, StatusCode.Unauthenticated)]
    [InlineData("denied", StatusCode.PermissionDenied)]
    [InlineData("allowed", StatusCode.OK)]
    public async Task StandardHostAuthorization_AppliesToDeltaMethods(string? credential, StatusCode expected)
    {
        await using var host = await StartAsync(authorize: true);
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v1", Message(ResourceTypeUrls.Route, "a")));
        var headers = new Metadata();
        if (credential is not null)
        {
            headers.Add("authorization", "Bearer " + credential);
        }
        using var call = Open(host.Channel, ResourceTypeUrls.Route, TestContext.Current.CancellationToken, headers);
        if (expected != StatusCode.OK)
        {
            var rejected = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
                await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
            });
            Assert.Equal(expected, rejected.StatusCode);
            Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
            Assert.False(host.Callbacks.Opened.Reader.TryRead(out _));
        }
        else
        {
            await call.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
            Assert.True(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            Assert.Equal("v1", call.ResponseStream.Current.SystemVersionInfo);
            var context = await Wait(host.Callbacks.Opened.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.True(context.GetHttpContext().User.Identity!.IsAuthenticated);
            Assert.Equal("node", context.GetHttpContext().User.FindFirst("xds-node")!.Value);
            await call.RequestStream.CompleteAsync();
            Assert.False(await Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        }
    }

    [Theory]
    [InlineData("client")]
    [InlineData("host")]
    public async Task Cancellation_ReleasesPendingDeltaWatchWithoutDisposingTheCache(string source)
    {
        await using var host = await StartAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var call = Open(host.Channel, "", cancellation.Token);
        await call.RequestStream.WriteAsync(Request(ResourceTypeUrls.Route), cancellation.Token);
        await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
        if (source == "client")
        {
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            Assert.Equal(StatusCode.Cancelled, canceled.StatusCode);
        }
        else
        {
            host.Application.Lifetime.StopApplication();
        }
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Null(host.Cache.Inner.GetStatusInfo("node"));
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Disposals);
        if (source == "host")
        {
            Assert.True(host.Application.Lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(host.Application.Lifetime.ApplicationStopped.IsCancellationRequested);
        }
        else
        {
            // A caller-owned client channel remains usable after one call is canceled.
            await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "after-cancel", Message(ResourceTypeUrls.Route, "a")));
            using var next = Open(host.Channel, ResourceTypeUrls.Route, TestContext.Current.CancellationToken);
            await next.RequestStream.WriteAsync(Request(), TestContext.Current.CancellationToken);
            Assert.True(await Wait(next.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
            Assert.Equal("after-cancel", next.ResponseStream.Current.SystemVersionInfo);
            await next.RequestStream.CompleteAsync();
            Assert.False(await Wait(next.ResponseStream.MoveNext(TestContext.Current.CancellationToken)));
        }
    }
}
