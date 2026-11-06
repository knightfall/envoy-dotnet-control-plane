using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class ValidationCoverageTests
{
    [Fact]
    public void CompleteEvidence_AcceptsDeclaredOutcomesAndCombination()
    {
        var (manifest, fixtures, catalog, report) = Evidence();

        ValidationCoverage.VerifyReports(manifest, fixtures, catalog, report);
    }

    [Theory]
    [InlineData("missing-case")]
    [InlineData("duplicate-case")]
    [InlineData("changed-expectation")]
    [InlineData("wrong-outcome")]
    [InlineData("fault-as-rejection")]
    [InlineData("missing-diagnostic")]
    [InlineData("missing-combination")]
    [InlineData("unknown-path")]
    [InlineData("uncovered-field")]
    [InlineData("changed-descriptor")]
    public void IncompleteOrChangedEvidence_IsRejected(string change)
    {
        var (manifest, fixtures, catalog, report) = Evidence();
        var rows = report["Results"]!.AsArray();
        switch (change)
        {
            case "missing-case": rows.RemoveAt(1); break;
            case "duplicate-case": rows[1] = rows[0]!.DeepClone(); break;
            case "changed-expectation": rows[1]!["ExpectedValid"] = true; break;
            case "wrong-outcome": rows[1]!["ActualValid"] = true; break;
            case "fault-as-rejection": rows[1]!["Error"] = "CompilationException"; break;
            case "missing-diagnostic": rows[1]!["Violations"] = new JsonArray(); break;
            case "missing-combination": catalog["combinations"] = new JsonArray(); break;
            case "unknown-path": rows[0]!["RulePaths"] = new JsonArray("uint32.gt"); break;
            case "uncovered-field": catalog["combinations"]![0]!["evidenceFields"] = new JsonArray("sample.Other.limit"); break;
            case "changed-descriptor": catalog["derivedDescriptorSha256"] = "new-derived"; break;
        }

        Assert.Throws<InvalidOperationException>(() => ValidationCoverage.VerifyReports(manifest, fixtures, catalog, report));
    }

    [Theory]
    [InlineData("originalDescriptors")]
    [InlineData("derivedDescriptors")]
    [InlineData("evaluator.cs")]
    [InlineData("coverage.lock.json")]
    public void ChangedInputs_InvalidatePreviouslyPassingEvidence(string input)
    {
        var inputs = JsonNode.Parse("""
            {"originalDescriptors":"old", "derivedDescriptors":"old", "evaluator.cs":"old", "coverage.lock.json":"old"}
            """)!.AsObject();
        var report = new JsonObject { ["catalogWideConformance"] = true, ["inputs"] = inputs.DeepClone() };
        Assert.True(ValidationCoverage.IsCurrent(report, inputs));

        inputs[input] = "new";

        Assert.False(ValidationCoverage.IsCurrent(report, inputs));
        report["inputs"] = inputs.DeepClone();
        report["catalogWideConformance"] = false;
        Assert.False(ValidationCoverage.IsCurrent(report, inputs));
    }

    [Theory]
    [InlineData("buf.pgv-go.gen.yaml")]
    [InlineData("tests/validation/PgvConformance/main.go.template")]
    [InlineData("tests/validation/PgvConformance/go.mod.template")]
    [InlineData("tests/validation/PgvConformance/go.sum.template")]
    public void ChangedGeneratorConfiguration_WithoutLockUpdate_InvalidatesCoverage(string input)
    {
        using var fixture = new UpstreamFixture();
        const string manifest = "conformance.lock.json";
        string path = Path.Combine(fixture.Root, input);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "original generator input");
        var configuration = new JsonObject
        {
            ["configurationSha256"] = new JsonObject { [input] = "unchanged expected hash" },
        };
        File.WriteAllText(Path.Combine(fixture.Root, manifest), configuration.ToJsonString());
        var before = new JsonObject();
        ValidationCoverage.RecordConfigurationInputs(before, fixture.Root, [manifest]);
        var report = new JsonObject { ["catalogWideConformance"] = true, ["inputs"] = before.DeepClone() };
        var unchanged = new JsonObject();
        ValidationCoverage.RecordConfigurationInputs(unchanged, fixture.Root, [manifest]);
        Assert.True(ValidationCoverage.IsCurrent(report, unchanged));

        File.WriteAllText(path, "changed generator input");
        var after = new JsonObject();
        ValidationCoverage.RecordConfigurationInputs(after, fixture.Root, [manifest]);

        Assert.Equal(before[manifest]!.GetValue<string>(), after[manifest]!.GetValue<string>());
        Assert.NotEqual(before[input]!.GetValue<string>(), after[input]!.GetValue<string>());
        Assert.False(ValidationCoverage.IsCurrent(report, after));
    }

    private static (JsonObject Manifest, JsonArray Fixtures, JsonObject Catalog, JsonObject Report) Evidence()
    {
        var manifest = JsonNode.Parse("""
            {"originalDescriptorSha256":"original", "derivedDescriptorSha256":"derived", "fieldRuleCount":1,
             "oneofOptionCount":0, "rulePathCount":1, "combinationCount":1, "fieldGroupCount":1, "fieldCaseCount":2}
            """)!.AsObject();
        var fixtures = JsonNode.Parse("""
            [{"field":"sample.Message.limit", "rulePaths":["uint32.lte"],
              "cases":[{"id":"boundary", "valid":true}, {"id":"outside", "valid":false}]}]
            """)!.AsArray();
        var catalog = JsonNode.Parse("""
            {"originalDescriptorSha256":"original", "derivedDescriptorSha256":"derived", "fieldRuleCount":1,
             "oneofOptionCount":0, "rulePathCounts":{"uint32.lte":1},
             "combinations":[{"paths":["uint32.lte"], "fields":["sample.Message.limit"], "evidenceFields":["sample.Message.limit"]}]}
            """)!.AsObject();
        var report = JsonNode.Parse("""
            {"Results":[
              {"Id":"sample.Message.limit/boundary", "Type":"sample.Message", "Field":"limit", "ExpectedValid":true,
               "ActualValid":true, "Error":null, "Violations":[], "RulePaths":["uint32.lte"]},
              {"Id":"sample.Message.limit/outside", "Type":"sample.Message", "Field":"limit", "ExpectedValid":false,
               "ActualValid":false, "Error":null, "Violations":["uint32.lte|limit"], "RulePaths":["uint32.lte"]}]}
            """)!.AsObject();
        return (manifest, fixtures, catalog, report);
    }
}
