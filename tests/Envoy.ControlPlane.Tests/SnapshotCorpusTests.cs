using Envoy.ControlPlane.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotCorpusTests
{
    [Fact]
    public void Corpus_SourceDerivedFixtures_MatchDeclaredOutcomes()
    {
        var fixtures = SnapshotCorpus.ReadFixtures();
        Assert.Equal(19, fixtures.Count);
        var results = fixtures.Select(fixture => SnapshotCorpus.Evaluate(fixture!)).ToArray();
        Assert.Equal(19, results.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
        for (int index = 0; index < results.Length; index++)
        {
            var fixture = fixtures[index]!;
            var actual = results[index];
            Assert.True(fixture["consistent"]!.GetValue<bool>() == actual.Consistent, actual.Id);
            Assert.Equal(fixture["names"]!.AsObject().Count, actual.ResourceNames.Count);
            foreach (var (type, names) in fixture["names"]!.AsObject())
            {
                Assert.Equal(names!.AsArray().Select(name => name!.GetValue<string>()), actual.ResourceNames[type]);
                Assert.Equal(fixture["version"]!.GetValue<string>(), actual.Versions[type]);
            }
        }
        SnapshotCorpus.WriteResults(results);
    }
}
