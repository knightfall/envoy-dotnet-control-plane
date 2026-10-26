// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
// Selected Delta lifecycle scenarios adapted to C#/xUnit; see ../resources/UPSTREAM-TESTS.md.
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class DeltaStreamTests
{
    [Fact]
    public async Task DefaultServer_DeliversSnapshotAndReleasesItsWatch()
    {
        var cache = new SnapshotCache();
        var snapshot = new Snapshot("v1", new Dictionary<string, IEnumerable<ResourceEntry>>
        {
            [ResourceTypeUrls.Route] = [new(new Envoy.Config.Route.V3.RouteConfiguration { Name = "a" })],
        });
        await cache.SetSnapshotAsync("node", snapshot);
        var input = new DeltaInput();
        var sent = new TaskCompletionSource<DeltaDiscoveryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new DeltaServer(cache, shutdown: TestContext.Current.CancellationToken);
        Task running = server.StreamAsync(input, (response, _) => { sent.SetResult(response); return ValueTask.CompletedTask; }, ResourceTypeUrls.Route);
        var request = DeltaStreamFixtures.Request(type: "", additions: ["a"]);
        request.Node = new Node { Id = "node" };
        input.Push(request);
        var response = await Wait(sent.Task);
        Assert.Equal("a", Assert.Single(response.Resources).Name);
        Assert.Equal("v1", response.SystemVersionInfo);
        Assert.Equal("1", response.Nonce);
        input.Complete();
        await Wait(running);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.Equal(1, input.Disposals);
    }

    [Fact]
    public async Task WatchReplacement_SettlesAsynchronousDisposalBeforeChangingSubscription()
    {
        await using var stream = new DeltaHarness();
        var entered = Signal();
        var release = Signal();
        int created = 0;
        stream.Watcher.Create = (request, subscription, writer) =>
        {
            created++;
            return created == 1
                ? new AsyncDeltaRegistration(subscription, entered, release)
                : new DeltaRegistration(request, subscription, writer);
        };
        stream.Start();
        stream.Input.Push(DeltaStreamFixtures.Request(additions: ["a"]));
        var first = await stream.NextWatch();
        stream.Input.Push(DeltaStreamFixtures.Request(additions: ["b"], removals: ["a"]));
        await Wait(entered.Task);
        Assert.Equal(["a"], first.Subscription.SubscribedResources);
        Assert.Equal(1, created);
        release.SetResult();
        var next = await stream.NextWatch();
        Assert.Same(first.Subscription, next.Subscription);
        Assert.Equal(["b"], next.Subscription.SubscribedResources);
        await stream.Finish();
    }

    [Fact]
    public async Task RequestCallbacks_RunBeforeNormalizationAndCaptureTheirNodeChanges()
    {
        await using var stream = new DeltaHarness();
        var state = new object();
        var replacement = new Node { Id = "callback-node" };
        var observed = new List<string>();
        stream.Callbacks.Open = (context, id, type, _) =>
        {
            Assert.Same(state, context.State);
            Assert.Equal(stream.Cancellation.Token, context.CancellationToken);
            Assert.Equal(1, id);
            Assert.Equal("implied", type);
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Request = (_, request, _) =>
        {
            observed.Add(request.TypeUrl + "|" + request.Node?.Id);
            if (request.TypeUrl == "before")
            {
                request.TypeUrl = "after";
                request.Node = replacement;
                request.ResourceNamesSubscribe.Add("selected");
            }
            return ValueTask.CompletedTask;
        };
        Node? closed = null;
        stream.Callbacks.Close = (_, node) => { closed = node; return ValueTask.CompletedTask; };
        stream.Start("implied", state: state);
        stream.Input.Push(DeltaStreamFixtures.Request(type: ""));
        var first = await stream.NextWatch();
        Assert.Equal("implied", first.Request.TypeUrl);
        Assert.Equal("", first.Request.Node.Id);
        stream.Input.Push(DeltaStreamFixtures.Request(type: "before"));
        var changed = await stream.NextWatch();
        Assert.Equal("after", changed.Request.TypeUrl);
        Assert.Same(replacement, changed.Request.Node);
        Assert.Equal(["selected"], changed.Subscription.SubscribedResources);
        stream.Input.Push(DeltaStreamFixtures.Request(type: ""));
        var carried = await stream.NextWatch();
        Assert.Same(replacement, carried.Request.Node);
        await stream.Finish();
        Assert.Same(replacement, closed);
        Assert.Equal(["|", "before|", "|"], observed);
        Assert.All(stream.Watcher.Registrations, row => Assert.Equal(1, row.Disposals));
        Assert.Equal(1, stream.Input.Disposals);
    }

    [Fact]
    public async Task EachType_SeedsVersionsOnceAndDoesNotFilterOldNoncesOrNacks()
    {
        await using var stream = new DeltaHarness();
        stream.Start(typeUrl: "");
        var firstRequest = DeltaStreamFixtures.Request(additions: ["a"]);
        firstRequest.InitialResourceVersions.Add("a", "initial-a");
        stream.Input.Push(firstRequest);
        var first = await stream.NextWatch();
        Assert.Equal("initial-a", first.Subscription.ReturnedResources["a"]);
        first.Respond(DeltaStreamFixtures.Response(first.Request, "delivered", "a"));
        Assert.Equal("1", (await stream.NextResponse()).Nonce);
        var secondType = DeltaStreamFixtures.Request(type: "type/B", additions: ["b"]);
        secondType.InitialResourceVersions.Add("b", "initial-b");
        stream.Input.Push(secondType);
        var second = await stream.NextWatch();
        Assert.Equal("initial-b", second.Subscription.ReturnedResources["b"]);
        second.Respond(DeltaStreamFixtures.Response(second.Request, "other", "b"));
        Assert.Equal("2", (await stream.NextResponse()).Nonce);
        var nack = DeltaStreamFixtures.Request(nonce: "old-unrelated", additions: ["new"]);
        nack.InitialResourceVersions.Add("a", "must-not-reset");
        nack.ErrorDetail = new Google.Rpc.Status { Code = 3, Message = "rejected" };
        stream.Input.Push(nack);
        var updated = await stream.NextWatch();
        Assert.Same(first.Subscription, updated.Subscription);
        Assert.Equal("delivered", updated.Subscription.ReturnedResources["a"]);
        Assert.Equal(["a", "new"], updated.Subscription.SubscribedResources.Order(StringComparer.Ordinal));
        Assert.Equal(1, first.Disposals);
        Assert.Equal(0, second.Disposals);
        await stream.Finish();
    }

    [Fact]
    public async Task BufferedResponses_DrainBeforeTheNextRequestCallbackAndSubscriptionUpdate()
    {
        await using var stream = new DeltaHarness();
        var entered = Signal();
        var release = Signal();
        var events = new List<string>();
        stream.Callbacks.Response = (_, _, response, _) =>
        {
            events.Add("response:" + response.SystemVersionInfo);
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Request = async (_, request, token) =>
        {
            events.Add("request:" + request.TypeUrl);
            if (request.TypeUrl == "hold")
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
        };
        stream.Start(typeUrl: "");
        stream.Input.Push(DeltaStreamFixtures.Request(additions: ["a"]));
        var first = await stream.NextWatch();
        stream.Input.Push(DeltaStreamFixtures.Request(type: "type/B", additions: ["b"]));
        var second = await stream.NextWatch();
        stream.Input.Push(DeltaStreamFixtures.Request(type: "hold"));
        await Wait(entered.Task);
        first.Respond(DeltaStreamFixtures.Response(first.Request, "queued-a", "a"));
        second.Respond(DeltaStreamFixtures.Response(second.Request, "queued-b", "b"));
        stream.Input.Push(DeltaStreamFixtures.Request(additions: ["new"], removals: ["a"]));
        release.SetResult();
        _ = await stream.NextWatch(); // Held request completes registration first.
        var replacement = await stream.NextWatch();
        Assert.Equal(["new"], replacement.Subscription.SubscribedResources);
        Assert.Empty(replacement.Subscription.ReturnedResources); // Delivery happened before removing a.
        Assert.Equal(["request:type/A", "request:type/B", "request:hold", "response:queued-a", "response:queued-b", "request:type/A"], events);
        Assert.Equal("1", (await stream.NextResponse()).Nonce);
        Assert.Equal("2", (await stream.NextResponse()).Nonce);
        await stream.Finish();
    }

    [Fact]
    public async Task ReturnedVersions_AdvanceOnlyAfterSuccessfulSend()
    {
        await using var stream = new DeltaHarness();
        var release = Signal();
        stream.Start();
        stream.Input.Push(DeltaStreamFixtures.Request(additions: ["a"]));
        var registration = await stream.NextWatch();
        var envelope = new DeltaDiscoveryResponse { TypeUrl = "type/A", SystemVersionInfo = "v", Nonce = "retained" };
        var response = new PassthroughDeltaResponse(registration.Request, envelope, new Dictionary<string, string> { ["a"] = "v" });
        stream.Callbacks.Response = (_, request, outgoing, _) =>
        {
            Assert.Same(registration.Request, request);
            Assert.Empty(registration.Subscription.ReturnedResources);
            Assert.Equal("1", outgoing.Nonce);
            outgoing.Nonce = "callback-nonce";
            return ValueTask.CompletedTask;
        };
        stream.Sending = async (_, token) =>
        {
            Assert.Empty(registration.Subscription.ReturnedResources);
            await release.Task.WaitAsync(token);
        };
        registration.Respond(response);
        Assert.Equal("callback-nonce", (await stream.NextResponse()).Nonce);
        Assert.Empty(registration.Subscription.ReturnedResources);
        stream.Input.Push(DeltaStreamFixtures.Request());
        release.SetResult();
        var next = await stream.NextWatch();
        Assert.Equal("v", next.Subscription.ReturnedResources["a"]);
        Assert.Equal("retained", envelope.Nonce);
        await stream.Finish();
    }

    [Theory]
    [InlineData("materialize")]
    [InlineData("send")]
    [InlineData("callback")]
    public async Task DeliveryFailure_PropagatesAndClosesOwnedResources(string stage)
    {
        await using var stream = new DeltaHarness();
        var failure = new InvalidDataException(stage);
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        stream.Start();
        stream.Input.Push(DeltaStreamFixtures.Request());
        var registration = await stream.NextWatch();
        if (stage == "send")
        {
            stream.Sending = (_, _) => ValueTask.FromException(failure);
        }
        if (stage == "callback")
        {
            stream.Callbacks.Response = (_, _, _, _) => ValueTask.FromException(failure);
        }
        registration.Respond(stage == "materialize"
            ? new FailingDeltaResponse(registration.Request, failure)
            : DeltaStreamFixtures.Response(registration.Request, "v", "a"));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidDataException>(() => Wait(stream.Running)));
        Assert.Empty(registration.Subscription.ReturnedResources);
        Assert.Equal(1, registration.Disposals);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Equal(1, closes);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("request")]
    [InlineData("ads-type")]
    public async Task RejectedLifecycle_ClosesOnceAndDisposesInput(string stage)
    {
        await using var stream = new DeltaHarness();
        var failure = new InvalidOperationException(stage);
        int requested = 0;
        int closed = 0;
        stream.Callbacks.Close = (_, node) => { closed++; Assert.Equal(stage == "ads-type" ? "client" : "", node.Id); return ValueTask.CompletedTask; };
        stream.Callbacks.Request = (_, _, _) =>
        {
            requested++;
            return stage == "request" ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        };
        if (stage == "open")
        {
            stream.Callbacks.Open = (_, _, _, _) => ValueTask.FromException(failure);
        }
        stream.Start(typeUrl: "");
        if (stage != "open")
        {
            var request = DeltaStreamFixtures.Request(type: "");
            request.Node = new Node { Id = "client" };
            stream.Input.Push(request);
        }
        if (stage == "ads-type")
        {
            var error = await Assert.ThrowsAsync<DeltaProtocolException>(() => Wait(stream.Running));
            Assert.Equal(DeltaProtocolError.InvalidArgument, error.Error);
        }
        else
        {
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Wait(stream.Running)));
        }
        Assert.Equal(stage == "open" ? 0 : 1, requested);
        Assert.Equal(1, closed);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Empty(stream.Watcher.Registrations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyWildcard_UsesGlobalAndPerTypeOptions(bool allTypes)
    {
        await using var stream = new DeltaHarness();
        stream.Start(typeUrl: "", options: new DeltaOptions
        {
            DisableLegacyWildcard = allTypes,
            LegacyWildcardDisabledTypes = new HashSet<string>(["type/A"], StringComparer.Ordinal),
        });
        stream.Input.Push(DeltaStreamFixtures.Request());
        Assert.False((await stream.NextWatch()).Subscription.IsWildcard);
        stream.Input.Push(DeltaStreamFixtures.Request(type: "type/B"));
        Assert.Equal(!allTypes, (await stream.NextWatch()).Subscription.IsWildcard);
        await stream.Finish();
    }

    [Fact]
    public async Task Cancellation_ReleasesThePendingWriteAndWatch()
    {
        await using var stream = new DeltaHarness();
        using var host = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stream.Sending = async (_, token) => await Task.Delay(System.Threading.Timeout.Infinite, token);
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        stream.Start(host: host);
        stream.Input.Push(DeltaStreamFixtures.Request());
        var registration = await stream.NextWatch();
        registration.Respond(DeltaStreamFixtures.Response(registration.Request, "v", "a"));
        _ = await stream.NextResponse();
        host.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(stream.Running));
        Assert.Empty(registration.Subscription.ReturnedResources);
        Assert.Equal(1, registration.Disposals);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task ClosedResponseWriter_FailsUnavailableAndReleasesEveryOwnedResource()
    {
        await using var stream = new DeltaHarness();
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.CompletedTask; };
        stream.Start(typeUrl: "");
        stream.Input.Push(DeltaStreamFixtures.Request());
        var first = await stream.NextWatch();
        stream.Input.Push(DeltaStreamFixtures.Request(type: "type/B"));
        var second = await stream.NextWatch();
        Assert.True(first.Responses.TryComplete());

        var failure = await Assert.ThrowsAsync<DeltaProtocolException>(() => Wait(stream.Running));
        Assert.Equal(DeltaProtocolError.Unavailable, failure.Error);
        Assert.Equal("resource channel closed", failure.Message);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Equal(1, closes);
        Assert.True(stream.Running.IsCompleted);
    }

    [Fact]
    public async Task CleanupFailures_RetainPrimaryErrorAndAttemptEveryOwnedReleaseOnce()
    {
        await using var stream = new DeltaHarness();
        var primary = new InvalidOperationException("request rejected");
        var firstFailure = new IOException("first watch release");
        var secondFailure = new IOException("second watch release");
        var inputFailure = new IOException("input release");
        var closeFailure = new IOException("close callback");
        var first = new FailingOwnedRegistration(firstFailure);
        var second = new FailingOwnedRegistration(secondFailure);
        stream.Watcher.Create = (request, _, _) => request.TypeUrl == "type/A" ? first : second;
        stream.Input.DisposalFailure = inputFailure;
        int closes = 0;
        stream.Callbacks.Close = (_, _) => { closes++; return ValueTask.FromException(closeFailure); };
        stream.Callbacks.Request = (_, request, _) => request.TypeUrl == "reject"
            ? ValueTask.FromException(primary) : ValueTask.CompletedTask;
        stream.Start(typeUrl: "");
        stream.Input.Push(DeltaStreamFixtures.Request());
        await stream.NextWatch();
        stream.Input.Push(DeltaStreamFixtures.Request(type: "type/B"));
        await stream.NextWatch();
        stream.Input.Push(DeltaStreamFixtures.Request(type: "reject"));

        var failure = await Assert.ThrowsAsync<AggregateException>(() => Wait(stream.Running));
        Assert.Equal(new Exception[] { primary, firstFailure, secondFailure, inputFailure, closeFailure }, failure.InnerExceptions);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, stream.Input.Disposals);
        Assert.Equal(1, closes);
        Assert.Equal(2, stream.Watcher.Registrations.Count);
        Assert.True(stream.Running.IsCompleted);
    }

    private sealed class FailingOwnedRegistration(Exception failure) : IDisposable, IAsyncDisposable
    {
        internal int Disposals { get; private set; }
        public void Dispose() => Assert.Fail("The stream must prefer asynchronous disposal");
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.FromException(failure);
        }
    }

    private sealed class FailingDeltaResponse(DeltaDiscoveryRequest request, Exception failure) : IDeltaResponse
    {
        public DeltaDiscoveryRequest Request => request;
        public string ResponseVersion => "v";
        public IReadOnlyDictionary<string, string> ReturnedResources => new Dictionary<string, string>();
        public ResponseContext Context => default;
        public DeltaDiscoveryResponse GetDeltaDiscoveryResponse() => throw failure;
    }

    private sealed class AsyncDeltaRegistration(ISubscription subscription, TaskCompletionSource entered,
        TaskCompletionSource release) : IDisposable, IAsyncDisposable
    {
        public void Dispose() => Assert.Fail("The stream must await asynchronous watch disposal");
        public async ValueTask DisposeAsync()
        {
            Assert.Equal(["a"], subscription.SubscribedResources);
            entered.SetResult();
            await Wait(release.Task);
            Assert.Equal(["a"], subscription.SubscribedResources);
        }
    }
}
