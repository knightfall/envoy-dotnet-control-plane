using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

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

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
