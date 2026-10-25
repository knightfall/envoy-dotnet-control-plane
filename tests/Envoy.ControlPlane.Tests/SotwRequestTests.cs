using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwRequestTests
{
    [Fact]
    public async Task DefaultServer_DeliversWithoutCallbacksAndReleasesItsWatch()
    {
        var watcher = new StreamWatcher();
        var input = new StreamInput();
        var sent = new TaskCompletionSource<Envoy.Service.Discovery.V3.DiscoveryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new SotwServer(watcher, shutdown: TestContext.Current.CancellationToken);
        Task running = server.StreamAsync(input, (response, _) => { sent.SetResult(response); return ValueTask.CompletedTask; }, "type/A");
        input.Push(Request());
        var watch = await Wait(watcher.Created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        watch.Watch.Respond(Response(watch.Request));
        Assert.Equal("1", (await Wait(sent.Task)).Nonce);
        input.Complete();
        await Wait(running);
        Assert.Equal(1, watch.Watch.Disposals);
        Assert.Equal(1, input.Disposals);
    }

    [Fact]
    public async Task Requests_CarryNodesAndImplyOnlyMissingTypes()
    {
        await using var stream = new StreamHarness();
        var events = new List<string>();
        var state = new object();
        stream.Callbacks.Open = (context, id, type, _) =>
        {
            Assert.Same(state, context.State);
            Assert.Equal(stream.Cancellation.Token, context.CancellationToken);
            Assert.Equal(1, id);
            events.Add("open:" + type);
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Request = (_, request, _) =>
        {
            events.Add("request:" + request.Node.Id + ":" + request.TypeUrl);
            return ValueTask.CompletedTask;
        };
        Node? closed = null;
        stream.Callbacks.Close = (_, node) => { closed = node; events.Add("close"); return ValueTask.CompletedTask; };
        stream.Start(typeUrl: "implied", state: state);
        stream.Input.Push(Request(type: ""));
        var first = await stream.NextWatch();
        Assert.Equal("", first.Request.Node.Id);
        Assert.Equal("implied", first.Request.TypeUrl);
        var supplied = Request(type: "other");
        supplied.Node = new Node { Id = "later" };
        stream.Input.Push(supplied);
        var second = await stream.NextWatch();
        stream.Input.Push(Request(type: ""));
        var third = await stream.NextWatch();
        Assert.Same(second.Request.Node, third.Request.Node);
        await stream.Finish();
        Assert.Same(supplied.Node, closed);
        Assert.Equal(["open:implied", "request::implied", "request:later:other", "request:later:implied", "close"], events);
        Assert.All(stream.Watcher.Registrations, registration => Assert.Equal(1, registration.Watch.Disposals));
    }

    [Fact]
    public async Task RequestCallback_ChangesDispatchButNodeReplacementDoesNotChangeCarriedReference()
    {
        await using var stream = new StreamHarness();
        var captured = new Node { Id = "captured" };
        stream.Callbacks.Request = (_, request, _) =>
        {
            if (request.TypeUrl == "before")
            {
                request.TypeUrl = "after";
                request.Node.Id = "mutated";
                request.Node = new Node { Id = "callback" };
                request.ResourceNames.Add("selected");
            }
            return ValueTask.CompletedTask;
        };
        stream.Start();
        var request = Request(type: "before");
        request.Node = captured;
        stream.Input.Push(request);
        var modified = await stream.NextWatch();
        Assert.Equal("after", modified.Request.TypeUrl);
        Assert.Equal("callback", modified.Request.Node.Id);
        Assert.Equal(["selected"], modified.Subscription.SubscribedResources);
        stream.Input.Push(Request(type: "next"));
        var next = await stream.NextWatch();
        Assert.Same(captured, next.Request.Node);
        Assert.Equal("mutated", next.Request.Node.Id);
        await stream.Finish();
    }

    [Fact]
    public async Task Server_AssignsDistinctIdsAcrossStreamsAndClosesEachOnce()
    {
        var opened = new List<long>();
        var closed = new List<long>();
        var callbacks = new StreamCallbacks
        {
            Open = (_, id, _, _) => { opened.Add(id); return ValueTask.CompletedTask; },
            Close = (id, _) => { closed.Add(id); return ValueTask.CompletedTask; },
        };
        var server = new SotwServer(new StreamWatcher(), callbacks, shutdown: TestContext.Current.CancellationToken);
        var first = new StreamInput();
        var second = new StreamInput();
        Task a = server.StreamAsync(first, (_, _) => ValueTask.CompletedTask, "type/A");
        Task b = server.StreamAsync(second, (_, _) => ValueTask.CompletedTask, "type/B");
        first.Complete();
        await Wait(a);
        second.Complete();
        await Wait(b);
        Assert.Equal([1L, 2L], opened);
        Assert.Equal([1L, 2L], closed);
    }

    [Fact]
    public async Task StaleRequest_IsCallbackVisibleAndCapturesNodeWithoutReplacingWatch()
    {
        await using var stream = new StreamHarness();
        var observed = new List<string>();
        stream.Callbacks.Request = (_, request, _) => { observed.Add(request.ResponseNonce); return ValueTask.CompletedTask; };
        stream.Start();
        stream.Input.Push(Request(names: ["a"]));
        var first = await stream.NextWatch();
        first.Watch.Respond(Response(first.Request, names: ["a"]));
        Assert.Equal("1", (await stream.NextResponse()).Nonce);
        var stale = Request(nonce: "old", names: ["ignored"]);
        stale.Node = new Node { Id = "stale-node" };
        stream.Input.Push(stale);
        stream.Input.Push(Request(nonce: "1", names: ["accepted"]));
        var accepted = await stream.NextWatch();
        Assert.Equal("stale-node", accepted.Request.Node.Id);
        Assert.Equal(["accepted"], accepted.Subscription.SubscribedResources);
        Assert.Equal(1, first.Watch.Disposals);
        Assert.Same(first.Subscription, accepted.Subscription);
        // The replacement has not responded, so an older nonce cannot reject this request.
        stream.Input.Push(Request(nonce: "unrelated", names: ["newest"]));
        var newest = await stream.NextWatch();
        Assert.Equal(["newest"], newest.Subscription.SubscribedResources);
        Assert.Equal(3, stream.Watcher.Registrations.Count);
        Assert.Equal(["", "old", "1", "unrelated"], observed);
        await stream.Finish();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyWildcard_UsesServerOptions(bool disabled)
    {
        await using var stream = new StreamHarness();
        stream.Start(options: new SotwOptions { DisableLegacyWildcard = disabled });
        stream.Input.Push(Request());
        var registration = await stream.NextWatch();
        Assert.Equal(!disabled, registration.Subscription.IsWildcard);
        await stream.Finish();
    }

    [Fact]
    public async Task MissingAdsType_RejectsBeforeRequestCallbackAndStillCloses()
    {
        await using var stream = new StreamHarness();
        int requests = 0;
        int closes = 0;
        stream.Callbacks.Request = (_, _, _) => { requests++; return ValueTask.CompletedTask; };
        stream.Callbacks.Close = (_, node) => { closes++; Assert.Equal("client", node.Id); return ValueTask.CompletedTask; };
        stream.Start(typeUrl: "");
        var request = Request(type: "");
        request.Node = new Node { Id = "client" };
        stream.Input.Push(request);
        var failure = await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running));
        Assert.Equal(SotwProtocolError.InvalidArgument, failure.Error);
        Assert.Equal(0, requests);
        Assert.Equal(1, closes);
        Assert.Empty(stream.Watcher.Registrations);
        Assert.Equal(1, stream.Input.Disposals);
    }
}
