using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class WatcherContractTests
{
    [Fact]
    public async Task CustomWatcher_RetainsRequestsSubscriptionMetadataAndResponseContext()
    {
        const string customType = "application/custom-type";
        var request = new DiscoveryRequest { TypeUrl = customType, VersionInfo = "previous" };
        var deltaRequest = new DeltaDiscoveryRequest { TypeUrl = customType, ResponseNonce = "nonce" };
        var returned = new Dictionary<string, string> { ["previous"] = "v0", ["current"] = "v1" };
        var subscription = new TestSubscription(new Dictionary<string, string> { ["previous"] = "v0" },
            new HashSet<string> { "current" }, new HashSet<string> { "collection/" }, true);
        using var cancellation = new CancellationTokenSource();
        var callerState = new object();
        var context = new ResponseContext(cancellation.Token, callerState);
        var discovery = new DiscoveryResponse { TypeUrl = customType, VersionInfo = "sotw/v1" };
        var deltaDiscovery = new DeltaDiscoveryResponse { TypeUrl = customType, SystemVersionInfo = "delta/v1" };
        var sotw = new TestSotwResponse(request, "sotw/v1", returned, context, () => discovery);
        var delta = new TestDeltaResponse(deltaRequest, "delta/v1", returned, context, deltaDiscovery);
        var implementation = new ImmediateWatcher(sotw, delta);
        IConfigWatcher watcher = implementation;

        using var sotwWatch = watcher.CreateWatch(request, subscription);
        Assert.Same(request, implementation.LastRequest);
        Assert.Same(subscription, implementation.LastSubscription);
        var response = await sotwWatch.Completion;
        Assert.Same(request, response.Request);
        Assert.Same(sotw, response);
        Assert.Equal(0, sotw.Materializations);
        Assert.Equal("sotw/v1", response.ResponseVersion);
        Assert.Same(returned, response.ReturnedResources);
        Assert.Equal("v0", response.ReturnedResources["previous"]);
        Assert.Same(discovery, response.GetDiscoveryResponse());
        Assert.Equal(1, sotw.Materializations);
        Assert.Same(callerState, response.Context.State);
        Assert.Equal(cancellation.Token, response.Context.CancellationToken);

        using var deltaWatch = watcher.CreateDeltaWatch(deltaRequest, subscription);
        Assert.Same(deltaRequest, implementation.LastRequest);
        Assert.Same(subscription, implementation.LastSubscription);
        var deltaResponse = await deltaWatch.Completion;
        Assert.Same(deltaRequest, deltaResponse.Request);
        Assert.Same(delta, deltaResponse);
        Assert.Equal("delta/v1", deltaResponse.ResponseVersion);
        Assert.Same(returned, deltaResponse.ReturnedResources);
        Assert.Same(deltaDiscovery, deltaResponse.GetDeltaDiscoveryResponse());
        Assert.Equal(context, deltaResponse.Context);
        Assert.Equal(new[] { "current" }, subscription.SubscribedResources);
        Assert.Equal(new[] { "collection/" }, subscription.SubscribedPrefixes);
        Assert.True(subscription.IsWildcard);
        Assert.Equal("v0", subscription.ReturnedResources["previous"]);
        cancellation.Cancel();
        Assert.True(response.Context.CancellationToken.IsCancellationRequested);
        Assert.True(deltaResponse.Context.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task MaterializationFailure_IsVisibleAfterSuccessfulWatchDelivery()
    {
        var failure = new InvalidOperationException("custom response cannot materialize");
        ISotwResponse response = new TestSotwResponse(new DiscoveryRequest(), "v1", new Dictionary<string, string>(),
            default, () => throw failure);
        using var watch = new Watch<ISotwResponse>();
        Assert.True(watch.TrySetResult(response));
        var delivered = await watch.Completion;
        Assert.Same(response, delivered);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(delivered.GetDiscoveryResponse));
    }

    [Fact]
    public void CreationFailure_IsSynchronousAndPreservesCustomWatcherException()
    {
        var failure = new InvalidOperationException("watch rejected");
        IConfigWatcher watcher = new RejectingWatcher(failure);
        var subscription = new TestSubscription(new Dictionary<string, string>(), new HashSet<string>(), new HashSet<string>(), false);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => watcher.CreateWatch(new DiscoveryRequest(), subscription)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => watcher.CreateDeltaWatch(new DeltaDiscoveryRequest(), subscription)));
    }
}
