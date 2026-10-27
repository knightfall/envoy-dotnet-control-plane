using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;
using DeltaCalls = Envoy.ControlPlane.AspNetCore.Tests.Support.DeltaDiscoveryCalls;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class ActiveCallbackShutdownTests
{
    [Theory]
    [InlineData(false, "request")]
    [InlineData(true, "request")]
    [InlineData(false, "response")]
    [InlineData(true, "response")]
    public async Task ApplicationStopping_CancelsActiveCallbackBeforeStopDeadline(bool ordered, string stage)
    {
        var entered = Signal();
        var canceled = Signal();
        var unreleased = Signal();
        await using var host = await StartAsync(new SotwOptions { OrderedAds = ordered }, configure: (cache, callbacks) =>
        {
            async ValueTask BlockAsync(CancellationToken token)
            {
                await cache.Inner.SetSnapshotAsync("callback", Snapshot(ResourceTypeUrls.Cluster, "reentered"));
                entered.TrySetResult();
                try
                {
                    await unreleased.Task.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }
            if (stage == "request")
            {
                callbacks.RequestHandler = (request, token) => request.TypeUrl == "hold" ? BlockAsync(token) : ValueTask.CompletedTask;
            }
            else
            {
                callbacks.ResponseHandler = (_, token) => BlockAsync(token);
            }
        });
        using var call = Open(host.Channel, "", TestContext.Current.CancellationToken);
        foreach (string type in new[] { ResourceTypeUrls.Cluster, ResourceTypeUrls.Route })
        {
            await call.RequestStream.WriteAsync(Request(type), TestContext.Current.CancellationToken);
            await Wait(host.Cache.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        }
        Assert.Equal(2, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        if (stage == "request")
        {
            await call.RequestStream.WriteAsync(Request("hold"), TestContext.Current.CancellationToken);
        }
        else
        {
            await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "v1"));
        }
        await Wait(entered.Task);
        Assert.Equal("reentered", host.Cache.Inner.GetSnapshot("callback").GetVersion(ResourceTypeUrls.Cluster));
        Assert.False(unreleased.Task.IsCompleted);

        // Signal the lifetime directly: no StopAsync deadline or client cancellation
        // can release the blocked callback before these cleanup observations.
        host.Application.Lifetime.StopApplication();
        await Wait(canceled.Task);
        await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.True(host.Application.Lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.False(host.Application.Lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.False(unreleased.Task.IsCompleted);
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumWatches);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("response")]
    public async Task DeltaApplicationStopping_CancelsActiveCallbackAndReleasesBothTypes(string stage)
    {
        var entered = Signal();
        var canceled = Signal();
        var unreleased = Signal();
        await using var host = await StartAsync(configure: (cache, callbacks) =>
        {
            async ValueTask BlockAsync(CancellationToken token)
            {
                await cache.Inner.SetSnapshotAsync("callback", DeltaCalls.Snapshot(ResourceTypeUrls.Cluster, "reentered"));
                entered.TrySetResult();
                try
                {
                    await unreleased.Task.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }
            if (stage == "request")
            {
                callbacks.DeltaRequestHandler = (request, token) => request.TypeUrl == "hold" ? BlockAsync(token) : ValueTask.CompletedTask;
            }
            else
            {
                callbacks.DeltaResponseHandler = (_, token) => BlockAsync(token);
            }
        });
        using var call = DeltaCalls.Open(host.Channel, "", TestContext.Current.CancellationToken);
        foreach (string type in new[] { ResourceTypeUrls.Cluster, ResourceTypeUrls.Route })
        {
            await call.RequestStream.WriteAsync(DeltaCalls.Request(type), TestContext.Current.CancellationToken);
            await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        }
        Assert.Equal(2, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
        if (stage == "request")
        {
            await call.RequestStream.WriteAsync(DeltaCalls.Request("hold"), TestContext.Current.CancellationToken);
        }
        else
        {
            await host.Cache.Inner.SetSnapshotAsync("node", DeltaCalls.Snapshot(ResourceTypeUrls.Cluster, "v1",
                DeltaCalls.Message(ResourceTypeUrls.Cluster, "a")));
        }
        await Wait(entered.Task);
        Assert.Equal("reentered", host.Cache.Inner.GetSnapshot("callback").GetVersion(ResourceTypeUrls.Cluster));

        host.Application.Lifetime.StopApplication();
        await Wait(canceled.Task);
        Assert.Equal("node", (await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).Id);
        Assert.True(host.Application.Lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.False(host.Application.Lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.False(unreleased.Task.IsCompleted);
        Assert.Equal(0, host.Cache.Inner.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Disposals);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("response")]
    public async Task FetchApplicationStopping_CancelsActiveHookBeforeStopDeadline(string stage)
    {
        var entered = Signal();
        var canceled = Signal();
        var unreleased = Signal();
        CancellationToken originalToken = default;
        async ValueTask BlockAsync(CancellationToken token)
        {
            entered.TrySetResult();
            try
            {
                await unreleased.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                canceled.TrySetResult();
                throw;
            }
        }
        await using var host = await StartAsync(configure: (_, callbacks) =>
        {
            callbacks.FetchRequestHandler = (context, _) =>
            {
                originalToken = context.CancellationToken;
                return stage == "request" ? BlockAsync(originalToken) : ValueTask.CompletedTask;
            };
            callbacks.FetchResponseAsync = (_, response) =>
            {
                Assert.NotNull(response);
                return BlockAsync(originalToken);
            };
        });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Route, "v1"));
        using var call = DiscoveryFetchCalls.OpenFetch(host.Channel, ResourceTypeUrls.Route, Request(), TestContext.Current.CancellationToken);
        await Wait(entered.Task);
        Assert.False(originalToken.IsCancellationRequested);

        host.Application.Lifetime.StopApplication();
        await Wait(canceled.Task);
        await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseAsync));
        Assert.True(originalToken.IsCancellationRequested);
        Assert.True(host.Application.Lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.False(host.Application.Lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.False(unreleased.Task.IsCompleted);
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
        Assert.False(host.Cache.Created.Reader.TryRead(out _));
        Assert.False(host.Callbacks.Opened.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Disposals);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
