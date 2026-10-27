using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class ReproducibilityDiffTests
{
    [Fact]
    public void SameInputsAndRules_ReportNoChangeWithoutApprovingValidation()
    {
        var before = Snapshot();
        var after = (JsonObject)before.DeepClone();
        after["revision"] = "new-library-commit";

        var report = ReproducibilityDiff.Create(before, after);

        Assert.False(report["hasChanges"]!.GetValue<bool>());
        Assert.All(report["changes"]!.AsObject(), section => Assert.Empty(section.Value!.AsArray()));
        Assert.False(report["validationAcceptance"]!.GetValue<bool>());
        Assert.Equal("not-assessed", report["coverage"]!["unsupportedConstraints"]!.GetValue<string>());
        Assert.Equal("new-library-commit", report["afterRevision"]!.GetValue<string>());
        Assert.Empty(report["verificationSelection"]!["goDotnetCorpora"]!.AsArray());
        Assert.Empty(report["verificationSelection"]!["commands"]!.AsArray());
    }

    [Fact]
    public void RulesAddedChangedAndRemoved_RetainExactOldAndNewValues()
    {
        var before = Snapshot();
        before["descriptors"]!["pgvRules"]!["field:sample.limit"] = JsonNode.Parse("{\"uint32\":{\"lte\":10}}");
        before["descriptors"]!["pgvRules"]!["field:sample.old"] = JsonNode.Parse("{\"bool\":{\"const\":false}}");
        var after = (JsonObject)before.DeepClone();
        after["descriptors"]!["pgvRules"]!["field:sample.limit"]!["uint32"]!["lte"] = 9;
        after["descriptors"]!["pgvRules"]!.AsObject().Remove("field:sample.old");
        after["descriptors"]!["pgvRules"]!["field:sample.new"] = JsonNode.Parse("{\"duration\":{\"gt\":\"0s\"}}");

        var report = ReproducibilityDiff.Create(before, after);
        var changes = report["changes"]!["pgvRules"]!.AsArray();

        Assert.True(report["hasChanges"]!.GetValue<bool>());
        Assert.Equal(new[] { "changed", "added", "removed" }, changes.Select(row => row!["change"]!.GetValue<string>()));
        Assert.Equal(10, changes[0]!["before"]!["uint32"]!["lte"]!.GetValue<int>());
        Assert.Equal(9, changes[0]!["after"]!["uint32"]!["lte"]!.GetValue<int>());
        Assert.Null(changes[1]!["before"]);
        Assert.Equal("0s", changes[1]!["after"]!["duration"]!["gt"]!.GetValue<string>());
        Assert.False(changes[2]!["before"]!["bool"]!["const"]!.GetValue<bool>());
        Assert.Null(changes[2]!["after"]);
        Assert.Contains("every declared Go/.NET validation case", report["requiredVerification"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inputs", "schemas/bindings.lock.json")]
    [InlineData("inputs", "schemas/validation/rules.recognized.json")]
    [InlineData("files", "envoy/config/route/v3/route.proto")]
    [InlineData("derivedFiles", "envoy/config/route/v3/route.proto")]
    [InlineData("derivedRules", "field:envoy.config.route.v3.VirtualHost.domains")]
    public void SourceToolSchemaAndDerivedChanges_AreReportedIndependently(string section, string key)
    {
        var before = Snapshot();
        JsonNode originalParent = section == "inputs" ? before : before["descriptors"]!;
        originalParent[section]![key] = "old-value";
        var after = (JsonObject)before.DeepClone();
        JsonNode parent = section == "inputs" ? after : after["descriptors"]!;
        parent[section]![key] = "new-value";

        var report = ReproducibilityDiff.Create(before, after);
        var change = Assert.Single(report["changes"]![section]!.AsArray())!;

        Assert.True(report["hasChanges"]!.GetValue<bool>());
        Assert.Equal(key, change["key"]!.GetValue<string>());
        Assert.Equal("changed", change["change"]!.GetValue<string>());
        Assert.Equal("old-value", change["before"]!.GetValue<string>());
        Assert.Equal("new-value", change["after"]!.GetValue<string>());
        Assert.False(report["validationAcceptance"]!.GetValue<bool>());
    }

    [Fact]
    public void IncompleteSnapshot_IsRejectedInsteadOfReportingNoChanges()
    {
        var before = Snapshot();
        before["descriptors"]!.AsObject().Remove("derivedRules");
        var error = Assert.Throws<InvalidOperationException>(() => ReproducibilityDiff.Create(before, Snapshot()));
        Assert.Contains("Missing descriptor snapshot section: derivedRules", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffCommand_WritesAnUpdateReportWithoutGeneratedBindings()
    {
        using var fixture = new UpstreamFixture();
        string beforePath = Path.Combine(fixture.Root, "before.json");
        string afterPath = Path.Combine(fixture.Root, "after.json");
        string output = Path.Combine(fixture.Root, "reports", "update.json");
        var before = Snapshot();
        before["inputs"]!["upstream-baseline.json"] = JsonNode.Parse("{\"envoyApi\":{\"commit\":\"old-schema-commit\"}}");
        var after = (JsonObject)before.DeepClone();
        after["inputs"]!["upstream-baseline.json"] = JsonNode.Parse("{\"envoyApi\":{\"commit\":\"new-schema-commit\"}}");
        File.WriteAllText(beforePath, before.ToJsonString());
        File.WriteAllText(afterPath, after.ToJsonString());

        var result = await fixture.Run("diff", beforePath, afterPath, output);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        var report = JsonNode.Parse(File.ReadAllText(output))!;
        var change = Assert.Single(report["changes"]!["inputs"]!.AsArray())!;
        Assert.Equal("upstream-baseline.json", change["key"]!.GetValue<string>());
        Assert.Equal("old-schema-commit", change["before"]!["envoyApi"]!["commit"]!.GetValue<string>());
        Assert.Equal("new-schema-commit", change["after"]!["envoyApi"]!["commit"]!.GetValue<string>());
        Assert.False(report["validationAcceptance"]!.GetValue<bool>());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "src")));
    }

    [Fact]
    public void RuntimeAndCorpusChanges_SelectFullCurrentReplayWithoutApprovingValidation()
    {
        var before = Snapshot();
        before["inputs"]!["src/Envoy.ControlPlane/Streams/DeltaSession.cs"] = "old-runtime";
        before["inputs"]!["tests/resources/GoOracle/delta_streams.go"] = "old-corpus";
        var after = (JsonObject)before.DeepClone();
        after["inputs"]!["src/Envoy.ControlPlane/Streams/DeltaSession.cs"] = "new-runtime";
        after["inputs"]!["tests/resources/GoOracle/delta_streams.go"] = "new-corpus";

        var report = ReproducibilityDiff.Create(before, after);

        Assert.Equal("old-runtime", before["inputs"]!["src/Envoy.ControlPlane/Streams/DeltaSession.cs"]!.GetValue<string>());
        var changes = report["changes"]!["inputs"]!.AsArray();
        Assert.Equal(new[] { "old-runtime", "old-corpus" }, changes.Select(row => row!["before"]!.GetValue<string>()));
        Assert.Equal(new[] { "new-runtime", "new-corpus" }, changes.Select(row => row!["after"]!.GetValue<string>()));
        var selection = report["verificationSelection"]!;
        Assert.Equal(new[] { "snapshot", "cache", "subscription", "helpers", "delta-subscription", "delta-cache",
            "linear-cache", "mux-cache", "heartbeats", "fetch", "fetch-engine", "gateway", "ads-client", "conversions",
            "delta-stream", "stream" }, selection["goDotnetCorpora"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal(new[] { "bindings generate --cache <source-cache>", "repository verify", "envoy verify" },
            selection["commands"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal("Complete solution and all fresh package consumers", selection["nativeTests"]!.GetValue<string>());
        Assert.Equal("Manual only; not selected", selection["performanceBaseline"]!.GetValue<string>());
        Assert.Contains("no command execution or passing result", selection["executionEvidence"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(report["validationAcceptance"]!.GetValue<bool>());
        Assert.Equal("not-assessed", report["coverage"]!["unsupportedConstraints"]!.GetValue<string>());
    }

    private static JsonObject Snapshot() => JsonNode.Parse("""
        {
          "schemaVersion": 1, "kind": "envoy-control-plane-reproducibility", "revision": "old-library-commit",
          "inputs": {},
          "descriptors": { "files": {}, "derivedFiles": {}, "pgvRules": {}, "derivedRules": {} },
          "coverage": { "catalogWideConformance": false, "unsupportedConstraints": "not-assessed" }
        }
        """)!.AsObject();
}
