using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class ValidationDifferenceTests
{
    [Fact]
    public void SeparateOutcomes_RequireAnExplicitReasonAndExactCaseInventory()
    {
        var corpus = Corpus();
        var fixtures = Fixtures();
        ValidationVerification.VerifyDeclaredDifferences(corpus, fixtures);

        corpus["acceptedDifferences"]![0]!["cases"]!.AsArray().Add("unreviewed-case");
        var error = Assert.Throws<InvalidOperationException>(() => ValidationVerification.VerifyDeclaredDifferences(corpus, fixtures));
        Assert.Contains("case inventory", error.Message);
    }

    [Theory]
    [InlineData("undeclared")]
    [InlineData("changed-outcome")]
    [InlineData("missing-reason")]
    public void UnapprovedOrChangedDifferences_AreRejected(string change)
    {
        var corpus = Corpus();
        var fixtures = Fixtures();
        switch (change)
        {
            case "undeclared":
                fixtures[1]!["difference"] = "not-approved";
                break;
            case "changed-outcome":
                corpus["acceptedDifferences"]![0]!["goValid"] = false;
                break;
            case "missing-reason":
                corpus["acceptedDifferences"]![0]!["reason"] = "";
                break;
        }
        Assert.Throws<InvalidOperationException>(() => ValidationVerification.VerifyDeclaredDifferences(corpus, fixtures));
    }

    [Fact]
    public void OrdinaryOutcome_CannotBeRelabeledAsAnAcceptedDifference()
    {
        var fixtures = Fixtures();
        fixtures[0]!["difference"] = "nan-range-rejection";
        var error = Assert.Throws<InvalidOperationException>(() => ValidationVerification.VerifyDeclaredDifferences(Corpus(), fixtures));
        Assert.Contains("equivalent case", error.Message);
    }

    private static JsonNode Corpus() => JsonNode.Parse("""
        {"acceptedDifferences":[{
          "id":"nan-range-rejection", "reason":"Deliberate upstream NaN rejection",
          "dotnetValid":false, "goValid":true, "cases":["percent-nan"]
        }]}
        """)!;

    private static JsonNode?[] Fixtures() => JsonNode.Parse("""
        [{"id":"percent-above", "valid":false},
         {"id":"percent-nan", "valid":false, "goValid":true, "difference":"nan-range-rejection"}]
        """)!.AsArray().ToArray();
}
