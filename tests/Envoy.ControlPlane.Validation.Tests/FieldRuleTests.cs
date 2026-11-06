using System.Text.Json;
using Envoy.ControlPlane.Validation.Tests.Support;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class FieldRuleTests
{
    public static IEnumerable<object[]> Cases => FieldRuleCorpus.Results.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(Cases))]
    public void FieldBoundary_MatchesDeclaredOutcome(string id)
    {
        var result = FieldRuleCorpus.Results.Single(row => row.Id == id);
        Assert.Null(result.Error);
        Assert.Equal(result.ExpectedValid, result.ActualValid);
        Assert.Equal(!result.ExpectedValid, result.Violations.Length > 0);
    }

    [Fact]
    public void FieldCorpus_RecordsAllOutcomesForGoComparison()
    {
        Assert.Equal(310, FieldRuleCorpus.Results.Length);
        Assert.Equal(310, FieldRuleCorpus.Results.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "validation-fields.json"),
            JsonSerializer.Serialize(new { Results = FieldRuleCorpus.Results }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
