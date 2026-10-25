using Envoy.ControlPlane.Cache;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class WatchTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ImmediateResponse_IsAvailableBeforeConsumerStartsAwaiting()
    {
        var response = new object();
        using var watch = new Watch<object>();
        Assert.True(watch.TrySetResult(response));
        Assert.True(watch.Completion.IsCompletedSuccessfully);
        Assert.Same(response, await watch.Completion);
    }

    [Fact]
    public async Task PendingResponse_ReleasesRegistrationOnceAndKeepsFirstResult()
    {
        int releases = 0;
        using var watch = new Watch<object>(() => releases++);
        var response = new object();
        Assert.False(watch.Completion.IsCompleted);
        Assert.True(watch.TrySetResult(response));
        Assert.Equal(1, releases);
        Assert.Same(response, await watch.Completion);
        Assert.False(watch.TrySetResult(new object()));
        Assert.False(watch.TrySetException(new InvalidOperationException("late")));
        watch.Dispose();
        watch.Dispose();
        Assert.Same(response, await watch.Completion);
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task ProducerFailure_PreservesExceptionAndReleasesRegistrationOnce()
    {
        int releases = 0;
        using var watch = new Watch<object>(() => releases++);
        var failure = new InvalidOperationException("cache failure");
        Assert.True(watch.TrySetException(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => watch.Completion));
        Assert.False(watch.TrySetException(new InvalidOperationException("late")));
        Assert.False(watch.TrySetResult(new object()));
        watch.Dispose();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => watch.Completion));
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task Disposal_CancelsPendingWatchAndRejectsLaterDelivery()
    {
        int releases = 0;
        using var watch = new Watch<object>(() => releases++);
        watch.Dispose();
        watch.Dispose();
        Assert.True(watch.Completion.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(() => watch.Completion);
        Assert.False(watch.TrySetResult(new object()));
        Assert.False(watch.TrySetException(new InvalidOperationException("late")));
        Assert.Equal(1, releases);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClaimedOutcome_WinsWhileRegistrationCleanupIsInProgress(bool responseFirst)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        int releases = 0;
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCleanup = new ManualResetEventSlim();
        using var watch = new Watch<object>(() =>
        {
            Interlocked.Increment(ref releases);
            cleanupEntered.TrySetResult();
            releaseCleanup.Wait(cancellationToken);
        });
        var response = new object();
        var winner = Task.Run(() =>
        {
            if (responseFirst)
            {
                Assert.True(watch.TrySetResult(response));
            }
            else
            {
                watch.Dispose();
            }
        }, cancellationToken);
        Task? loser = null;
        try
        {
            await cleanupEntered.Task.WaitAsync(TestTimeout, cancellationToken);
            loser = Task.Run(() =>
            {
                if (responseFirst)
                {
                    watch.Dispose();
                }
                else
                {
                    Assert.False(watch.TrySetResult(response));
                }
            }, cancellationToken);
            await loser.WaitAsync(TestTimeout, cancellationToken);
            Assert.False(watch.Completion.IsCompleted);
            Assert.Equal(1, releases);
        }
        finally
        {
            releaseCleanup.Set();
            await winner.WaitAsync(TestTimeout, cancellationToken);
            if (loser is not null)
            {
                await loser.WaitAsync(TestTimeout, cancellationToken);
            }
        }
        if (responseFirst)
        {
            Assert.Same(response, await watch.Completion);
        }
        else
        {
            await Assert.ThrowsAsync<TaskCanceledException>(() => watch.Completion);
        }
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task BlockedConsumer_DoesNotRunInsideProducerOrBlockOtherWatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var watch = new Watch<string>();
        using var other = new Watch<string>();
        var consumerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseConsumer = new ManualResetEventSlim();
        var consumer = watch.Completion.ContinueWith(_ =>
        {
            consumerEntered.SetResult();
            releaseConsumer.Wait(cancellationToken);
        }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var producer = Task.Run(() => watch.TrySetResult("first"), cancellationToken);
        try
        {
            await consumerEntered.Task.WaitAsync(TestTimeout, cancellationToken);
            Assert.True(await producer.WaitAsync(TestTimeout, cancellationToken));
            Assert.False(consumer.IsCompleted);
            Assert.True(other.TrySetResult("second"));
            Assert.Equal("second", await other.Completion.WaitAsync(TestTimeout, cancellationToken));
            using var canceled = new Watch<string>();
            canceled.Dispose();
            await Assert.ThrowsAsync<TaskCanceledException>(() => canceled.Completion);
        }
        finally
        {
            releaseConsumer.Set();
            await consumer.WaitAsync(TestTimeout, cancellationToken);
            await producer.WaitAsync(TestTimeout, cancellationToken);
        }
        Assert.Equal("first", await watch.Completion);
    }
}
