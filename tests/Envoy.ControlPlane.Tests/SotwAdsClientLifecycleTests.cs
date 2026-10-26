using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Clients;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.SotwAdsClientTests;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwAdsClientLifecycleTests
{
    [Fact]
    public async Task PendingRead_SerializesAckUntilTheResponseArrives()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        await client.InitializeAsync(invoker);
        var call = Assert.Single(invoker.Calls);
        var receiving = client.FetchAsync(TestContext.Current.CancellationToken);
        await Wait(call.Reading.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        var acknowledging = client.AckAsync(TestContext.Current.CancellationToken);
        Assert.False(acknowledging.IsCompleted);
        Assert.Single(call.Requests);
        call.Enqueue(Response("v1", "nonce-1"));
        Assert.Empty(await Wait(receiving));
        await Wait(acknowledging);
        Assert.Equal(2, call.Requests.Count);
        Assert.Equal("v1", call.Requests[^1].VersionInfo);
        Assert.Equal("nonce-1", call.Requests[^1].ResponseNonce);
        Assert.Equal(1, call.Reads);
    }

    [Fact]
    public async Task CancelingQueuedAck_DoesNotAdvanceAcceptanceOrCancelTheActiveRead()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        await client.InitializeAsync(invoker);
        var call = Assert.Single(invoker.Calls);
        var receiving = client.FetchAsync(TestContext.Current.CancellationToken);
        await Wait(call.Reading.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        using var stop = new CancellationTokenSource();
        var acknowledging = client.AckAsync(stop.Token);
        stop.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(acknowledging));
        Assert.False(SotwAdsClient.IsConnectionError(error));
        Assert.False(receiving.IsCompleted);
        Assert.Single(call.Requests);
        call.Enqueue(Response("unaccepted", "latest"));
        await Wait(receiving);
        await client.NackAsync("not accepted", TestContext.Current.CancellationToken);
        Assert.Empty(call.Requests[^1].VersionInfo);
        Assert.Equal("latest", call.Requests[^1].ResponseNonce);
        Assert.Equal(0, call.Disposals);
    }

    [Fact]
    public async Task Dispose_CancelsTheOwnedReadAndRejectsQueuedAndFutureOperations()
    {
        var invoker = new ScriptedAdsInvoker();
        var client = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        await client.InitializeAsync(invoker);
        var call = Assert.Single(invoker.Calls);
        var receiving = client.FetchAsync(TestContext.Current.CancellationToken);
        await Wait(call.Reading.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        var queued = client.AckAsync(TestContext.Current.CancellationToken);
        client.Dispose();
        client.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(receiving));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Wait(queued));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.FetchAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.NackAsync("disposed", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.InitializeAsync(invoker));
        Assert.Equal(1, call.Disposals);
        Assert.Single(invoker.Calls);
    }

    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    private static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
