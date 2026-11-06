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
        Console.WriteLine($"Verified {cases} whole-message cases plus the declared field-rule coverage against Go PGV, including locked accepted differences.");
    }

    internal static async Task VerifyInputsAsync()
    {
        VerifiedValidationSources.VerifyMaterialized();
        ValidationConformance.VerifyOutputs();
        PgvConformance.VerifyOutputs();
        ValidationCoverage.VerifyInputs();
        await BuildProcess.RunAsync("dotnet", ["run", "--project", "tools/Envoy.ControlPlane.SchemaTools", "--configuration", "Release", "--no-restore",
            "--", "rules-check", BindingPaths.InputsDirectory + "/catalog.pb", "schemas/validation/rules.recognized.json"]);
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
        PgvConformance.VerifyReport();
        var corpus = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["corpus"]!;
        const string report = "tests/Envoy.ControlPlane.Validation.Tests/bin/Release/net10.0/validation-corpus.json";
        var results = JsonNode.Parse(File.ReadAllText(report))!;
        var rows = results["Results"]!.AsArray();
        Require(rows.Count == corpus["caseCount"]!.GetValue<int>() && rows.All(row =>
            row!["ActualValid"]!.GetValue<bool>() == row["ExpectedValid"]!.GetValue<bool>()), "Source evaluator differs from the independent fixture contract");
        var fixtures = JsonNode.Parse(File.ReadAllText(corpus["path"]!.GetValue<string>()))!.AsArray()
            .ToDictionary(row => row!["id"]!.GetValue<string>(), StringComparer.Ordinal);
        VerifyDeclaredDifferences(corpus, fixtures.Values.ToArray());
        Require(rows.Select(row => row!["Id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Keys.Order(StringComparer.Ordinal)), "Source report omitted or duplicated a corpus case");
        foreach (var row in rows)
        {
            var fixture = fixtures[row!["Id"]!.GetValue<string>()]!;
            bool expectedGo = fixture["goValid"]?.GetValue<bool>() ?? fixture["valid"]!.GetValue<bool>();
            Require(row["Type"]!.GetValue<string>() == fixture["type"]!.GetValue<string>()
                && row["ExpectedValid"]!.GetValue<bool>() == fixture["valid"]!.GetValue<bool>()
                && row["ExpectedGoValid"]!.GetValue<bool>() == expectedGo
                && row["Difference"]?.GetValue<string>() == fixture["difference"]?.GetValue<string>(),
                "Source report changed the declared fixture contract");
        }
        string oracle = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/validation/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(oracle)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", Path.GetFullPath(report), oracle], "tests/validation/GoOracle");

        await ValidationCoverage.VerifyAsync();

        return rows.Count;
    }

    internal static void VerifyDeclaredDifferences(JsonNode corpus, IReadOnlyList<JsonNode?> fixtures)
    {
        var differences = corpus["acceptedDifferences"]!.AsArray()
            .ToDictionary(item => item!["id"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var fixture in fixtures)
        {
            bool expected = fixture!["valid"]!.GetValue<bool>();
            bool expectedGo = fixture["goValid"]?.GetValue<bool>() ?? expected;
            string? differenceId = fixture["difference"]?.GetValue<string>();
            if (expected == expectedGo)
            {
                Require(differenceId is null, "An equivalent case cannot declare an accepted difference");
                continue;
            }
            Require(differenceId is not null && differences.ContainsKey(differenceId), "Unapproved validation difference");
            var difference = differences[differenceId!];
            Require(!string.IsNullOrWhiteSpace(difference!["reason"]?.GetValue<string>())
                && difference["dotnetValid"]!.GetValue<bool>() == expected
                && difference["goValid"]!.GetValue<bool>() == expectedGo, "Accepted difference changed its declared outcomes");
        }
        foreach (var (id, difference) in differences)
        {
            var actual = fixtures.Where(item => item!["difference"]?.GetValue<string>() == id)
                .Select(item => item!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal);
            var declared = difference!["cases"]!.AsArray().Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
            Require(declared.Length > 0 && actual.SequenceEqual(declared), "Accepted difference case inventory changed");
        }
    }
}
