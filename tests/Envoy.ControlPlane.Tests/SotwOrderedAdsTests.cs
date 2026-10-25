// Copyright 2018 Envoyproxy Authors. Licensed under Apache-2.0.
// Modified: selected ADS scenarios ported to C#/xUnit; see ../resources/UPSTREAM-TESTS.md.
using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwOrderedAdsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateUnsubscribedResponse_IsFilteredAgainstReplacementSubscription(bool ordered)
    {
        await using var stream = new StreamHarness();
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = ordered });
        stream.Input.Push(Request(names: ["old"]));
        var first = await stream.NextWatch();
        first.Watch.Respond(Response(first.Request, names: ["old"]));
        Assert.Equal("1", (await stream.NextResponse()).Nonce);
        var inspected = Signal();
        var late = Response(first.Request, version: "stale", names: ["old"]);
        stream.Watcher.Create = (_, _, responses) =>
        {
            var watch = new StreamWatch(responses);
            watch.Respond(new InspectedResponse(late, inspected));
            return watch;
        };
        stream.Input.Push(Request(nonce: "1", names: ["new"]));
        var replacement = await stream.NextWatch();
        await Wait(inspected.Task);
        // Another request is a barrier after the filtering decision, without a timing delay.
        stream.Watcher.Create = null;
        stream.Input.Push(Request("barrier"));
        await stream.NextWatch();
        Assert.Equal(0, late.Materializations);
        Assert.Empty(replacement.Subscription.ReturnedResources);
        Assert.Equal(new[] { "new" }, replacement.Subscription.SubscribedResources);
        Assert.False(stream.Sent.Reader.TryRead(out _));
        await stream.Finish();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestCallbackTypeMutation_UsesModeSpecificTypeCapture(bool ordered)
    {
        await using var stream = new StreamHarness();
        stream.Callbacks.Request = (_, request, _) =>
        {
            request.TypeUrl = "changed";
            return ValueTask.CompletedTask;
        };
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = ordered });
        stream.Input.Push(Request());
        var watch = await stream.NextWatch();
        watch.Watch.Respond(Response(watch.Request));
        if (ordered)
        {
            var failure = await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running));
            Assert.Equal(SotwProtocolError.Unavailable, failure.Error);
            Assert.Equal("No current watch for changed", failure.InnerException!.Message);
        }
        else
        {
            Assert.Equal("changed", (await stream.NextResponse()).TypeUrl);
            await stream.Finish();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReadinessAndOrdering_AreIndependentOptions(bool readiness, bool ordered)
    {
        var cache = new SnapshotCache(ads: readiness);
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Route,
            [new RouteConfiguration { Name = "a" }, new RouteConfiguration { Name = "b" }])));
        await using var stream = new StreamHarness();
        var entered = Signal();
        var release = Signal();
        stream.Callbacks.Request = async (_, request, token) =>
        {
            if (request.TypeUrl == "barrier")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "", cache: cache, options: new SotwOptions { OrderedAds = ordered });
        var first = Request(ResourceTypeUrls.Route, names: ["a"]);
        first.Node = new Node { Id = "node" };
        stream.Input.Push(first);
        if (!readiness)
        {
            var partial = await stream.NextResponse();
            Assert.Single(partial.Resources);
            Assert.Equal("a", RouteConfiguration.Parser.ParseFrom(partial.Resources[0].Value).Name);
        }
        stream.Input.Push(Request("barrier"));
        await Wait(entered.Task);
        Assert.False(stream.Sent.Reader.TryRead(out _));
        release.TrySetResult();
        stream.Input.Push(Request(ResourceTypeUrls.Route, readiness ? "" : "1", "a", "b"));
        var complete = await stream.NextResponse();
        Assert.Equal("v1", complete.VersionInfo);
        Assert.Equal(new[] { "a", "b" }, complete.Resources.Select(payload => RouteConfiguration.Parser.ParseFrom(payload.Value).Name).Order(StringComparer.Ordinal));
        await stream.Finish();
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Fact]
    public async Task SharedQueue_SendsProducerOrderAcrossTypes()
    {
        await using var stream = new StreamHarness();
        var entered = Signal();
        var release = Signal();
        stream.Sending = async (response, token) =>
        {
            if (response.TypeUrl == "gate")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = true });
        stream.Input.Push(Request("gate"));
        var gate = await stream.NextWatch();
        stream.Input.Push(Request("type/B"));
        var second = await stream.NextWatch();
        stream.Input.Push(Request("type/A"));
        var first = await stream.NextWatch();
        gate.Watch.Respond(Response(gate.Request));
        await Wait(entered.Task);
        Assert.Equal("gate", (await stream.NextResponse()).TypeUrl);
        first.Watch.Respond(Response(first.Request));
        second.Watch.Respond(Response(second.Request));
        release.TrySetResult();
        var outgoingA = await stream.NextResponse();
        var outgoingB = await stream.NextResponse();
        Assert.Equal("type/A", outgoingA.TypeUrl);
        Assert.Equal("type/B", outgoingB.TypeUrl);
        Assert.Equal("2", outgoingA.Nonce);
        Assert.Equal("3", outgoingB.Nonce);
        await stream.Finish();
    }

    [Fact]
    public async Task Replacement_DrainsOtherTypesAndDropsSupersededCurrentType()
    {
        await using var stream = new StreamHarness();
        var entered = Signal();
        var release = Signal();
        stream.Callbacks.Request = async (_, request, token) =>
        {
            if (request.VersionInfo == "replacement")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = true });
        stream.Input.Push(Request(names: ["old"]));
        var current = await stream.NextWatch();
        stream.Input.Push(Request("type/B"));
        var other = await stream.NextWatch();
        var replacement = Request(names: ["new"]);
        replacement.VersionInfo = "replacement";
        stream.Input.Push(replacement);
        await Wait(entered.Task);
        var superseded = Response(current.Request, names: ["old"]);
        current.Watch.Respond(superseded);
        other.Watch.Respond(Response(other.Request));
        release.TrySetResult();
        var next = await stream.NextWatch();
        Assert.Equal("type/B", (await stream.NextResponse()).TypeUrl);
        Assert.Equal(0, superseded.Materializations);
        Assert.Equal(1, current.Watch.Disposals);
        Assert.Same(current.Subscription, next.Subscription);
        Assert.False(Assert.IsType<SotwSubscription>(next.Subscription).MatchesResponse(superseded));
        next.Watch.Respond(Response(next.Request, names: ["new"]));
        Assert.Equal("type/A", (await stream.NextResponse()).TypeUrl);
        await stream.Finish();
        Assert.False(stream.Sent.Reader.TryRead(out _));
    }

    [Fact]
    public async Task StaleRequest_DoesNotDrainOrReplaceItsWatch()
    {
        await using var stream = new StreamHarness();
        var entered = Signal();
        var release = Signal();
        stream.Callbacks.Request = async (_, request, token) =>
        {
            if (request.ResponseNonce == "stale")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = true });
        stream.Input.Push(Request(names: ["old"]));
        var first = await stream.NextWatch();
        first.Watch.Respond(Response(first.Request, names: ["old"]));
        Assert.Equal("1", (await stream.NextResponse()).Nonce);
        stream.Input.Push(Request(nonce: "1", names: ["old"]));
        var current = await stream.NextWatch();
        current.Watch.Respond(Response(current.Request, version: "v2", names: ["old"]));
        Assert.Equal("2", (await stream.NextResponse()).Nonce);
        stream.Input.Push(Request("type/B"));
        var other = await stream.NextWatch();
        stream.Input.Push(Request(nonce: "stale", names: ["new"]));
        await Wait(entered.Task);
        other.Watch.Respond(Response(other.Request));
        release.TrySetResult();
        Assert.Equal("type/B", (await stream.NextResponse()).TypeUrl);
        Assert.Equal(0, current.Watch.Disposals);
        Assert.True(Assert.IsType<SotwSubscription>(current.Subscription).MatchesResponse(Response(current.Request, names: ["old"])));
        Assert.False(Assert.IsType<SotwSubscription>(current.Subscription).MatchesResponse(Response(current.Request, names: ["new"])));
        Assert.Equal(3, stream.Watcher.Registrations.Count);
        await stream.Finish();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendFailure_UsesSourceMainQueueAndDrainErrorBehavior(bool duringDrain)
    {
        await using var stream = new StreamHarness();
        var failure = new IOException("send");
        var entered = Signal();
        var release = Signal();
        stream.Sending = (_, _) => ValueTask.FromException(failure);
        stream.Callbacks.Request = async (_, request, token) =>
        {
            if (request.VersionInfo == "replacement")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = true });
        stream.Input.Push(Request());
        await stream.NextWatch();
        stream.Input.Push(Request("type/B"));
        var other = await stream.NextWatch();
        if (duringDrain)
        {
            var request = Request();
            request.VersionInfo = "replacement";
            stream.Input.Push(request);
            await Wait(entered.Task);
        }
        other.Watch.Respond(Response(other.Request));
        release.TrySetResult();
        if (duringDrain)
        {
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Wait(stream.Running)));
        }
        else
        {
            var wrapped = await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running));
            Assert.Equal(SotwProtocolError.Unavailable, wrapped.Error);
            Assert.Same(failure, wrapped.InnerException);
        }
        Assert.All(stream.Watcher.Registrations, registration => Assert.Equal(1, registration.Watch.Disposals));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomTypes_ExceedKnownFamilyCountAndKeepIndependentNonceState(bool ordered)
    {
        await using var stream = new StreamHarness();
        stream.Start(typeUrl: "", options: new SotwOptions { OrderedAds = ordered });
        for (int index = 0; index < 16; index++)
        {
            string type = "custom/" + index;
            stream.Input.Push(Request(type));
            var watch = await stream.NextWatch();
            watch.Watch.Respond(Response(watch.Request));
            var sent = await stream.NextResponse();
            Assert.Equal(type, sent.TypeUrl);
            Assert.Equal((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), sent.Nonce);
        }
        stream.Input.Push(Request("custom/0", nonce: "1"));
        var replacement = await stream.NextWatch();
        Assert.Equal("custom/0", replacement.Request.TypeUrl);
        Assert.Equal(1, stream.Watcher.Registrations.First().Watch.Disposals);
        Assert.Equal(0, stream.Watcher.Registrations.ElementAt(1).Watch.Disposals);
        await stream.Finish();
    }
}
