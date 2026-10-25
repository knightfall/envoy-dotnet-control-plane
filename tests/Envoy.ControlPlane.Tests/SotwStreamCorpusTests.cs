using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwStreamCorpusTests
{
    [Fact]
    public async Task SourceTraces_MatchDeclaredCallbacksWatchesAndSends()
    {
        var fixtures = SotwStreamCorpus.ReadFixtures();
        Assert.Equal(6, fixtures.Count);
        Assert.Equal(6, fixtures.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        var results = new JsonArray();
        foreach (var fixture in fixtures)
        {
            var observed = await SotwStreamCorpus.EvaluateAsync(fixture!);
            Assert.True(JsonNode.DeepEquals(fixture!["expected"], observed), fixture["id"] + "\nExpected: " + fixture["expected"] + "\nObserved: " + observed);
            var result = fixture.DeepClone().AsObject();
            result["observed"] = observed;
            results.Add(result);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sotw-stream-corpus.json"), results.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
