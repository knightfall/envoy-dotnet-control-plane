using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotCacheConcurrencyTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RegistrationAndPublication_RetainReplacementWhenLookupIsInProgress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var lookupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseLookup = new ManualResetEventSlim();
        var first = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]));
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", new ObservedSnapshot(first, () =>
        {
            lookupEntered.SetResult();
            releaseLookup.Wait(cancellationToken);
        }));
        var registration = Task.Run(() => cache.CreateWatch(Request(ResourceTypeUrls.Listener, "v1"),
            Subscription(wildcard: true, returned: new Dictionary<string, string> { ["listener"] = "v1" })), cancellationToken);
        Task? publication = null;
        try
        {
            await lookupEntered.Task.WaitAsync(TestTimeout, cancellationToken);
            publication = Task.Run(async () =>
            {
                publicationStarted.SetResult();
                await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v2", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }])));
            }, cancellationToken);
            await publicationStarted.Task.WaitAsync(TestTimeout, cancellationToken);
        }
        finally
        {
            releaseLookup.Set();
        }
        using var watch = await registration.WaitAsync(TestTimeout, cancellationToken);
        Assert.NotNull(publication);
        await publication.WaitAsync(TestTimeout, cancellationToken);
        var response = await watch.Completion.WaitAsync(TestTimeout, cancellationToken);
        Assert.Equal("v2", response.ResponseVersion);
        Assert.Equal("v2", response.ReturnedResources["listener"]);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        Assert.Equal("v2", cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Listener));
    }

    [Fact]
    public async Task BlockedConsumer_DoesNotHoldPublicationOrOtherNodeProgress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cache = new SnapshotCache();
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var consumerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseConsumer = new ManualResetEventSlim();
        var consumer = watch.Completion.ContinueWith(_ =>
        {
            consumerEntered.SetResult();
            releaseConsumer.Wait(cancellationToken);
        }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var publication = Task.Run(async () => await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1",
            (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]))), cancellationToken);
        try
        {
            await consumerEntered.Task.WaitAsync(TestTimeout, cancellationToken);
            await publication.WaitAsync(TestTimeout, cancellationToken);
            Assert.False(consumer.IsCompleted);
            Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
            using var other = cache.CreateWatch(Request(ResourceTypeUrls.Listener, nodeId: "other"), Subscription(wildcard: true));
            await cache.SetSnapshotAsync("other", ResourceFixtures.Snapshot("other/v1", (ResourceTypeUrls.Listener, [new Listener { Name = "other" }])));
            Assert.Equal("other/v1", (await other.Completion.WaitAsync(TestTimeout, cancellationToken)).ResponseVersion);
            Assert.Equal(0, cache.GetStatusInfo("other")!.NumWatches);
        }
        finally
        {
            releaseConsumer.Set();
            await consumer.WaitAsync(TestTimeout, cancellationToken);
            await publication.WaitAsync(TestTimeout, cancellationToken);
        }
    }
}
