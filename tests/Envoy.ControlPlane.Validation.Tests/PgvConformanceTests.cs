using System.Text.Json;
using Envoy.ControlPlane.Validation.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class PgvConformanceTests
{
    [Fact]
    public void PinnedUpstreamCases_RecordSelectionAndMatchGo()
    {
        var report = PgvConformanceCorpus.Report;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "validation-pgv-conformance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Assert.Equal("92b9a7df69ca9f71bfc492f7a90adf4d36eab569", report.SourceCommit);
        Assert.Equal(1053, report.Cases.Count);
        Assert.Equal(report.Cases.Count, report.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(report.ExpectedSelectionHash, report.ActualSelectionHash);
        Assert.Equal(report.ExpectedSelected, report.Cases.Count(item => item.Status == "passed"));
        Assert.DoesNotContain(report.Cases, item => item.Status == "failed");
        Assert.All(report.Cases.Where(item => item.Status == "excluded"), item =>
        {
            Assert.NotEmpty(item.UnsupportedRules);
            Assert.Null(item.ActualValid);
        });
    }

    public static IEnumerable<object[]> SelectedCases()
    {
        return PgvConformanceCorpus.Report.Cases.Where(item => item.UnsupportedRules.Length == 0)
            .Select(item => new object[] { item.Id, item.Name });
    }

    [Theory]
    [MemberData(nameof(SelectedCases), DisableDiscoveryEnumeration = true)]
    public void UpstreamCase_MatchesActualGoOutcome(string id, string name)
    {
        var item = Assert.Single(PgvConformanceCorpus.Report.Cases, candidate => candidate.Id == id && candidate.Name == name);
        Assert.True(item.Status == "passed", $"{item.Type}: expected valid={item.ExpectedValid}, actual={item.ActualValid}; {item.Error}");
    }
}
