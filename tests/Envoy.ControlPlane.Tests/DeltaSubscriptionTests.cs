using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class DeltaSubscriptionTests
{
    [Fact]
    public void SourceDerivedTransitions_MatchEveryDeclaredState()
    {
        var fixtures = DeltaSubscriptionCorpus.ReadFixtures();
        Assert.Equal(12, fixtures.Count);
        var results = fixtures.Select(fixture => DeltaSubscriptionCorpus.Evaluate(fixture!)).ToArray();
        Assert.Equal(12, results.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
        for (int index = 0; index < results.Length; index++)
        {
            var expected = fixtures[index]!["expected"];
            var observed = JsonSerializer.SerializeToNode(results[index].Observed, DeltaSubscriptionCorpus.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected, observed), results[index].Id + "\nExpected: " + expected + "\nObserved: " + observed);
        }
        DeltaSubscriptionCorpus.WriteResults(results);
    }

    [Fact]
    public void InitialAndReturnedVersions_UseOwnedBookkeepingForReadOnlyInputs()
    {
        var initial = new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["a"] = "accepted" });
        var subscription = new DeltaSubscription(["*", "a"], initialResourceVersions: initial);
        subscription.UpdateResourceSubscriptions([], ["a"]);
        Assert.Equal("", subscription.ReturnedResources["a"]);
        Assert.Equal("accepted", initial["a"]);
        Assert.NotSame(initial, subscription.ReturnedResources);

        var returned = new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["b"] = "new" });
        subscription.SetReturnedResources(returned);
        subscription.UpdateResourceSubscriptions(["b"], ["b"]);
        Assert.Equal("", subscription.ReturnedResources["b"]);
        Assert.False(subscription.ReturnedResources.ContainsKey("a"));
        Assert.Equal("new", returned["b"]);
        Assert.NotSame(returned, subscription.ReturnedResources);
    }

    [Fact]
    public void TypeAndReconnectStates_SeedIndependentlyWithoutReseedingUpdates()
    {
        var first = new DeltaSubscription(["a"], initialResourceVersions: new Dictionary<string, string> { ["a"] = "initial" });
        var otherType = new DeltaSubscription(["b"], initialResourceVersions: new Dictionary<string, string> { ["b"] = "other" });
        first.SetReturnedResources(new Dictionary<string, string> { ["a"] = "delivered" });
        first.UpdateResourceSubscriptions([], []);
        Assert.Equal("delivered", first.ReturnedResources["a"]);
        Assert.Equal("other", Assert.Single(otherType.ReturnedResources).Value);

        var reconnect = new DeltaSubscription(["a"], initialResourceVersions: new Dictionary<string, string> { ["a"] = "client-accepted" });
        Assert.Equal("client-accepted", reconnect.ReturnedResources["a"]);
        Assert.Equal("delivered", first.ReturnedResources["a"]);
    }
}
