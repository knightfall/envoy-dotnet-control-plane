using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwLifecycleTests
{
    [Theory]
    [InlineData("eof")]
    [InlineData("receive-error")]
    [InlineData("call-cancel")]
    [InlineData("host-cancel")]
    public async Task InputTermination_ReleasesWatchesAndClosesOnce(string termination)
    {
        await using var stream = new StreamHarness();
        using var host = new CancellationTokenSource();
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        stream.Start(host: host);
        stream.Input.Push(Request());
        var watch = await stream.NextWatch();
        switch (termination)
        {
            case "eof": stream.Input.Complete(); break;
            case "receive-error": stream.Input.Complete(new IOException("receive")); break;
            case "call-cancel": stream.Cancellation.Cancel(); break;
            case "host-cancel": host.Cancel(); break;
        }
        await Wait(stream.Running);
        Assert.Equal(1, watch.Watch.Disposals);
        Assert.True(watch.Watch.Completion.IsCanceled);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Equal(1, closes);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("request")]
    [InlineData("cache")]
    [InlineData("watch")]
    [InlineData("materialize")]
    [InlineData("response-callback")]
    [InlineData("send")]
    public async Task OperationFailure_PreservesOriginalAndCleansUp(string operation)
    {
        await using var stream = new StreamHarness();
        var failure = new IOException(operation);
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        if (operation == "open") stream.Callbacks.Open = (_, _, _, _) => ValueTask.FromException(failure);
        if (operation == "request") stream.Callbacks.Request = (_, _, _) => ValueTask.FromException(failure);
        if (operation == "cache") stream.Watcher.Create = (_, _) => throw failure;
        if (operation == "response-callback") stream.Callbacks.Response = (_, _, _, _, _) => ValueTask.FromException(failure);
        if (operation == "send") stream.Sending = (_, _) => ValueTask.FromException(failure);
        stream.Start();
        stream.Input.Push(Request());
        if (operation is "watch" or "materialize" or "response-callback" or "send")
        {
            var watch = await stream.NextWatch();
            if (operation == "watch")
            {
                watch.Watch.Fail(failure);
            }
            else if (operation == "materialize")
            {
                watch.Watch.Respond(new TestSotwResponse(watch.Request, "v1", new Dictionary<string, string>(), default, () => throw failure));
            }
            else
            {
                watch.Watch.Respond(Response(watch.Request));
            }
        }
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Wait(stream.Running)));
        Assert.Equal(1, closes);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.All(stream.Watcher.Registrations, registration => Assert.Equal(1, registration.Watch.Disposals));
        if (operation != "send") Assert.False(stream.Sent.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("response")]
    [InlineData("canceled-watch")]
    public async Task MissingStreamValue_ReturnsUnavailable(string missing)
    {
        await using var stream = new StreamHarness();
        stream.Start();
        stream.Input.Push(missing == "request" ? null! : Request());
        if (missing != "request")
        {
            var watch = await stream.NextWatch();
            if (missing == "response") watch.Watch.Respond(null!);
            else watch.Watch.Cancel();
        }
        var failure = await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running));
        Assert.Equal(SotwProtocolError.Unavailable, failure.Error);
        Assert.Equal(1, stream.Input.Disposals);
    }

    [Fact]
    public async Task CleanupFailures_RetainPrimaryAndAttemptEveryReleaseAndClose()
    {
        var primary = new IOException("request");
        var release = new InvalidOperationException("watch disposal");
        var iterator = new InvalidOperationException("iterator disposal");
        var close = new InvalidOperationException("close");
        await using var stream = new StreamHarness(new StreamInput { DisposalFailure = iterator });
        stream.Watcher.Create = (_, _) => new StreamWatch { DisposalFailure = release };
        int closes = 0;
        stream.Callbacks.Request = (_, request, _) => request.TypeUrl == "reject" ? ValueTask.FromException(primary) : ValueTask.CompletedTask;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.FromException(close); };
        stream.Start();
        stream.Input.Push(Request());
        var first = await stream.NextWatch();
        stream.Watcher.Create = null;
        stream.Input.Push(Request(type: "other"));
        var second = await stream.NextWatch();
        stream.Input.Push(Request(type: "reject"));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => Wait(stream.Running));
        Assert.Equal<Exception>([primary, release, iterator, close], failure.InnerExceptions);
        Assert.Equal(1, first.Watch.Disposals);
        Assert.Equal(1, second.Watch.Disposals);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task EnumeratorCreationFailure_IsNotSwallowedAsReceiveFailure()
    {
        var failure = new IOException("enumerator creation");
        await using var stream = new StreamHarness(new StreamInput { CreationFailure = failure });
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        stream.Start();
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Wait(stream.Running)));
        Assert.Equal(1, closes);
        Assert.Equal(0, stream.Input.Disposals);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("current-type")]
    [InlineData("body")]
    public async Task MalformedCustomResponse_FailsWithoutSendingAndReleasesWatch(string missing)
    {
        await using var stream = new StreamHarness();
        stream.Start();
        stream.Input.Push(Request());
        var watch = await stream.NextWatch();
        var origin = missing switch
        {
            "request" => null!,
            "current-type" => Request(type: "absent"),
            _ => watch.Request,
        };
        var response = new TestSotwResponse(origin, "v1", new Dictionary<string, string>(), default,
            () => missing == "body" ? null! : new Envoy.Service.Discovery.V3.DiscoveryResponse());
        watch.Watch.Respond(response);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Wait(stream.Running));
        Assert.Equal(missing switch
        {
            "request" => "Missing request in response",
            "current-type" => "No current watch for absent",
            _ => "Missing discovery response",
        }, failure.Message);
        Assert.Equal(1, watch.Watch.Disposals);
        Assert.False(stream.Sent.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperationCancellation_IsPreservedEvenAfterCallCancellation(bool fromCallback)
    {
        await using var stream = new StreamHarness();
        var failure = new OperationCanceledException("operation", stream.Cancellation.Token);
        ValueTask Reject()
        {
            stream.Cancellation.Cancel();
            return ValueTask.FromException(failure);
        }
        if (fromCallback) stream.Callbacks.Response = (_, _, _, _, _) => Reject();
        else stream.Sending = (_, _) => Reject();
        stream.Start();
        stream.Input.Push(Request());
        var watch = await stream.NextWatch();
        watch.Watch.Respond(Response(watch.Request));
        Assert.Same(failure, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(stream.Running)));
        Assert.Equal(1, watch.Watch.Disposals);
        Assert.Equal(1, stream.Input.Disposals);
    }

    [Fact]
    public async Task BlockedWriter_HoldsOneReceiveAndSerializesOtherTypeResponse()
    {
        await using var stream = new StreamHarness();
        var release = Signal();
        var entered = Signal();
        stream.Sending = async (response, token) =>
        {
            if (response.TypeUrl == "type/A")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "");
        stream.Input.Push(Request());
        var first = await stream.NextWatch();
        stream.Input.Push(Request(type: "type/B"));
        var second = await stream.NextWatch();
        first.Watch.Respond(Response(first.Request));
        await Wait(entered.Task);
        await stream.NextResponse();
        Assert.Equal(3, stream.Input.ReadAttempts);
        stream.Input.Push(Request(type: "held"));
        stream.Input.Push(Request(type: "still-queued"));
        second.Watch.Respond(Response(second.Request));
        Assert.Equal(3, stream.Input.ReadAttempts);
        Assert.False(stream.Sent.Reader.TryRead(out _));
        Assert.Equal(2, stream.Watcher.Registrations.Count);
        release.TrySetResult();
        Assert.Equal("type/B", (await stream.NextResponse()).TypeUrl);
        await stream.NextWatch();
        await stream.NextWatch();
        await stream.Finish();
        Assert.All(stream.Watcher.Registrations, registration => Assert.Equal(1, registration.Watch.Disposals));
    }
}
