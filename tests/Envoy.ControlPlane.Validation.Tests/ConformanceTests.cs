using System.Text.Json;
using Buf.Validate;
using Envoy.ControlPlane.Validation.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class ConformanceTests
{
    [Fact]
    public void PinnedUpstreamCases_MatchOutcomesAndStructuredFieldDiagnostics()
    {
        var report = ConformanceCorpus.Evaluate();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "validation-conformance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");

        Assert.Equal("88e9c909e82c569bba7d6d722dd5c6b76ba9e965", report.SourceCommit);
        Assert.Equal(38, report.Suites);
        Assert.Equal(2755, report.Cases.Count);
        Assert.Equal(report.Cases.Count, report.Cases.Select(item => item.Suite + "/" + item.Case).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2748, report.Cases.Count(item => item.Status == "passed"));
        var excluded = report.Cases.Where(item => item.Status == "excluded").ToArray();
        Assert.Equal(7, excluded.Length);
        Assert.All(excluded, item =>
        {
            Assert.Equal("standard_rules/well_known_types/timestamp", item.Suite);
            Assert.Null(item.Actual);
            Assert.Contains("exporter timestamp ages out", item.Detail);
        });
        Assert.DoesNotContain(report.Cases, item => item.Status == "failed");
        Assert.Equal(35, ConformanceCorpus.Files.Count);
        Assert.Contains(ConformanceCorpus.Files.SelectMany(file => file.Dependencies),
            file => ReferenceEquals(file, FieldRules.Descriptor.File));
    }
}
