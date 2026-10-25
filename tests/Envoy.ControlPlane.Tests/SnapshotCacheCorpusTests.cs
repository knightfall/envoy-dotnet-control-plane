using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotCacheCorpusTests
{
    [Fact]
    public void SourceDerivedEventTraces_MatchDeclaredOutcomes()
    {
        var fixtures = SnapshotCacheCorpus.ReadFixtures();
        Assert.Equal(22, fixtures.Count);
        var results = fixtures.Select(fixture => SnapshotCacheCorpus.Evaluate(fixture!)).ToArray();
        Assert.Equal(22, results.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
        for (int index = 0; index < results.Length; index++)
        {
            var expected = fixtures[index]!["expected"];
            var observed = JsonSerializer.SerializeToNode(results[index].Observed, SnapshotCacheCorpus.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected, observed), results[index].Id + "\nExpected: " + expected + "\nObserved: " + observed);
        }
        SnapshotCacheCorpus.WriteResults(results);
    }
}
