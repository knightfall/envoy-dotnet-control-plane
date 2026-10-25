using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwSubscriptionTests
{
    [Fact]
    public void SourceDerivedTransitions_MatchEveryDeclaredState()
    {
        var fixtures = SotwSubscriptionCorpus.ReadFixtures();
        Assert.Equal(14, fixtures.Count);
        var results = fixtures.Select(fixture => SotwSubscriptionCorpus.Evaluate(fixture!)).ToArray();
        Assert.Equal(14, results.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
        for (int index = 0; index < results.Length; index++)
        {
            var expected = fixtures[index]!["expected"];
            var observed = JsonSerializer.SerializeToNode(results[index].Observed, SotwSubscriptionCorpus.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected, observed), results[index].Id + "\nExpected: " + expected + "\nObserved: " + observed);
        }
        SotwSubscriptionCorpus.WriteResults(results);
    }

    [Fact]
    public void ReturnedMetadata_UsesOwnedBookkeepingWithReadOnlyInputs()
    {
        var versions = new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["a"] = "v1", ["other"] = "v0" });
        var subscription = new SotwSubscription(["a"]);
        subscription.SetReturnedResources(versions);
        Assert.Equal("v0", subscription.ReturnedResources["other"]);
        subscription.SetResourceSubscription(["a"]);
        Assert.Equal("a", Assert.Single(subscription.ReturnedResources).Key);
        Assert.Equal("v1", subscription.ReturnedResources["a"]);
        Assert.Equal("v0", versions["other"]);
        Assert.NotSame(versions, subscription.ReturnedResources);
    }

    [Theory]
    [InlineData(new string[] { }, new string[] { }, true)]
    [InlineData(new string[] { }, new[] { "a" }, false)]
    [InlineData(new[] { "a" }, new[] { "a" }, true)]
    [InlineData(new[] { "a" }, new[] { "a", "b" }, false)]
    [InlineData(new[] { "*" }, new[] { "anything" }, true)]
    [InlineData(new[] { "collection/*" }, new[] { "collection/a", "collection/sub/b" }, true)]
    [InlineData(new[] { "collection/*" }, new[] { "collection/a", "collection-other" }, false)]
    [InlineData(new[] { "a", "collection/*" }, new[] { "a", "collection/b" }, true)]
    public void ResponseMatching_RequiresEveryReturnedNameToRemainSubscribed(string[] subscribed, string[] returnedNames, bool matches)
    {
        var subscription = new SotwSubscription(subscribed, allowLegacyWildcard: false);
        var response = new TestSotwResponse(new DiscoveryRequest(), "v1",
            returnedNames.ToDictionary(name => name, _ => "opaque", StringComparer.Ordinal), default, () => new DiscoveryResponse());
        Assert.Equal(matches, subscription.MatchesResponse(response));
        Assert.Equal(0, response.Materializations);
    }

    [Fact]
    public void LegacyOptions_UseOrdinalTypeUrlsEvenWithCallerSetComparer()
    {
        var options = new SotwOptions
        {
            LegacyWildcardDisabledTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "APPLICATION/custom" },
        };
        Assert.True(options.IsLegacyWildcardActive("application/custom"));
        Assert.False(options.IsLegacyWildcardActive("APPLICATION/custom"));
        var globallyDisabled = new SotwOptions { DisableLegacyWildcard = true };
        Assert.False(globallyDisabled.IsLegacyWildcardActive("arbitrary/type"));
    }

    [Fact]
    public async Task UnsubscribeAndResubscribe_ReturnsResourceWithoutSnapshotVersionChange()
    {
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Route, [new RouteConfiguration { Name = "a" }])));
        var subscription = new SotwSubscription(["a"]);
        using var first = cache.CreateWatch(Request(ResourceTypeUrls.Route), subscription);
        Assert.True(first.Completion.IsCompletedSuccessfully);
        var firstResponse = await first.Completion;
        subscription.SetReturnedResources(firstResponse.ReturnedResources);
        first.Dispose();
        subscription.SetResourceSubscription([]);
        Assert.False(subscription.IsWildcard);
        Assert.Empty(subscription.ReturnedResources);
        using var empty = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), subscription);
        Assert.False(empty.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        empty.Dispose();
        subscription.SetResourceSubscription(["a"]);
        using var resubscribed = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), subscription);
        Assert.True(resubscribed.Completion.IsCompletedSuccessfully);
        var response = await resubscribed.Completion;
        Assert.Equal("v1", response.ResponseVersion);
        Assert.Equal("v1", response.ReturnedResources["a"]);
        Assert.Equal("a", Assert.Single(response.GetDiscoveryResponse().Resources).Unpack<RouteConfiguration>().Name);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }
}
