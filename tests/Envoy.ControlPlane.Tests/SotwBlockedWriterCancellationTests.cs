using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwBlockedWriterCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallCancellation_SettlesBlockedWriterAndBothTypeWatches(bool ordered)
    {
        await using var stream = new StreamHarness();
        var entered = Signal();
        var unreleased = Signal();
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        stream.Sending = async (_, token) =>
        {
            entered.TrySetResult();
            await unreleased.Task.WaitAsync(token);
        };
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = ordered });
        stream.Input.Push(Request());
        var first = await stream.NextWatch();
        stream.Input.Push(Request(type: "type/B"));
        var second = await stream.NextWatch();
        first.Watch.Respond(Response(first.Request));
        await Wait(entered.Task);
        Assert.Equal("type/A", (await stream.NextResponse()).TypeUrl);
        second.Watch.Respond(Response(second.Request));
        Assert.False(stream.Running.IsCompleted);
        Assert.False(stream.Sent.Reader.TryRead(out _));
        stream.Cancellation.Cancel();
        if (ordered)
        {
            var failure = await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running));
            Assert.Equal(SotwProtocolError.Unavailable, failure.Error);
            Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(stream.Running));
        }
        Assert.True(stream.Running.IsCompleted);
        Assert.False(unreleased.Task.IsCompleted);
        Assert.False(stream.Sent.Reader.TryRead(out _));
        Assert.Equal(1, first.Watch.Disposals);
        Assert.Equal(1, second.Watch.Disposals);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Equal(1, closes);
    }
}
