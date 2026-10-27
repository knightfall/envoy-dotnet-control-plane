using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ValidationVerification
{
    internal static async Task RunAsync()
    {
        await VerifyInputsAsync();
        await PackageVerification.RunTestsAsync(ValidationPaths.TestsProject, "validation");
        int cases = await CompareWithGoAsync();
        await PackageVerification.BuildPackageAsync();
        await PackageVerification.PackAsync(ValidationPaths.EvaluatorProject);
        await PackageVerification.VerifyCleanConsumerAsync(ValidationPaths.TestsProject, "validation-consumer");
        Console.WriteLine($"Source-built ProtoValidate matched Go PGV for all {cases} declared cases; full T26/GV coverage remains separate.");
    }

    internal static async Task VerifyInputsAsync()
    {
        VerifiedValidationSources.VerifyMaterialized();
        ValidationConformance.VerifyOutputs();
        var bindingLock = JsonNode.Parse(File.ReadAllText(ValidationPaths.BindingsLock))!;
        Require(GetFileSha256(ValidationPaths.MetadataRoot + "/catalog.pb") == bindingLock["descriptorSetSha256"]!.GetValue<string>(),
            "Generate the locked validation metadata before verification");
        foreach (var (path, hash) in bindingLock["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == hash!.GetValue<string>(), "Validation configuration differs: " + path);
        }
        VerifyGeneratedSources(BindingPaths.GeneratedDirectory, bindingLock["generatedSourceCount"]!.GetValue<int>(),
            bindingLock["generatedSourcesSha256"]!.GetValue<string>());
        VerifyGeneratedSources(ValidationPaths.AnnotationsRoot + "/generated", bindingLock["annotationSourceCount"]!.GetValue<int>(),
            bindingLock["annotationSourcesSha256"]!.GetValue<string>());
        var corpus = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["corpus"]!;
        Require(GetFileSha256(corpus["path"]!.GetValue<string>()) == corpus["sha256"]!.GetValue<string>(), "Validation corpus differs from lock");
        string version = await BuildProcess.RunAsync("go", ["version"]);
        Require(version.StartsWith("go version go" + corpus["goVersion"]!.GetValue<string>() + " ", StringComparison.Ordinal),
            "Go oracle toolchain differs from lock");
    }

    internal static async Task<int> CompareWithGoAsync()
    {
        ValidationConformance.VerifyReport();
        var corpus = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["corpus"]!;
        const string report = "tests/Envoy.ControlPlane.Validation.Tests/bin/Release/net10.0/validation-corpus.json";
        var results = JsonNode.Parse(File.ReadAllText(report))!;
        var rows = results["Results"]!.AsArray();
        Require(rows.Count == corpus["caseCount"]!.GetValue<int>() && rows.All(row =>
            row!["ActualValid"]!.GetValue<bool>() == row["ExpectedValid"]!.GetValue<bool>()), "Source evaluator differs from the independent fixture contract");
        var fixtures = JsonNode.Parse(File.ReadAllText(corpus["path"]!.GetValue<string>()))!.AsArray()
            .ToDictionary(row => row!["id"]!.GetValue<string>(), StringComparer.Ordinal);
        Require(rows.Select(row => row!["Id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Keys.Order(StringComparer.Ordinal)), "Source report omitted or duplicated a corpus case");
        foreach (var row in rows)
        {
            var fixture = fixtures[row!["Id"]!.GetValue<string>()]!;
            Require(row["Type"]!.GetValue<string>() == fixture["type"]!.GetValue<string>()
                && row["ExpectedValid"]!.GetValue<bool>() == fixture["valid"]!.GetValue<bool>(), "Source report changed the declared fixture contract");
        }
        string oracle = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/validation/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(oracle)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", Path.GetFullPath(report), oracle], "tests/validation/GoOracle");

        return rows.Count;
    }
}
