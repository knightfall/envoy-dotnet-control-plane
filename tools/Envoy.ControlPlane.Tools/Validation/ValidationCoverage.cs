using System.Text.Json;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ValidationCoverage
{
    internal const string ManifestPath = "tests/Envoy.ControlPlane.Validation.Tests/Fixtures/coverage.lock.json";
    internal const string CasesPath = "tests/Envoy.ControlPlane.Validation.Tests/Fixtures/field-cases.json";
    internal const string Output = BindingPaths.InputsDirectory + "/results/validation/coverage.json";
    private const string TestOutput = "tests/Envoy.ControlPlane.Validation.Tests/bin/Release/net10.0/";

    internal static void VerifyInputs()
    {
        var manifest = Read(ManifestPath);
        Require(manifest["schemaVersion"]?.GetValue<int>() == 1, "Unsupported validation coverage manifest");
        foreach (var (path, hash) in manifest["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == hash!.GetValue<string>(), "Validation coverage input changed: " + path);
        }
        Require(GetFileSha256(BindingPaths.InputsDirectory + "/catalog.pb") == manifest["originalDescriptorSha256"]!.GetValue<string>()
            && GetFileSha256(ValidationPaths.MetadataRoot + "/catalog.pb") == manifest["derivedDescriptorSha256"]!.GetValue<string>(),
            "Validation coverage does not match the current catalog");
    }

    internal static async Task VerifyAsync()
    {
        VerifyInputs();
        var manifest = Read(ManifestPath);
        var catalog = Read(TestOutput + "validation-coverage-catalog.json");
        var fields = Read(TestOutput + "validation-fields.json");
        VerifyReports(manifest, JsonNode.Parse(File.ReadAllText(CasesPath))!.AsArray(), catalog, fields);
        string oracle = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/validation/go-fields.json");
        Directory.CreateDirectory(Path.GetDirectoryName(oracle)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", Path.GetFullPath(TestOutput + "validation-fields.json"), oracle],
            "tests/validation/GoOracle");
        var coverage = new JsonObject
        {
            ["gate"] = "GV declared rule scope verified",
            ["scope"] = manifest["scope"]!.DeepClone(),
            ["catalogWideConformance"] = true,
            ["unsupportedConstraints"] = new JsonArray(),
            ["messageOneofAndDerivedConformance"] = "Verified within the pinned catalog and declared compatibility exceptions",
            ["inputs"] = CurrentInputs(),
            ["catalog"] = catalog,
            ["fieldCaseCount"] = fields["Results"]!.AsArray().Count,
            ["acceptedDifferences"] = Read(ValidationPaths.SourcesLock)["corpus"]!["acceptedDifferences"]!.DeepClone(),
            ["exclusions"] = manifest["exclusions"]!.DeepClone(),
        };
        File.WriteAllText(Output, coverage.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Copy(TestOutput + "validation-fields.json", Path.Combine(Path.GetDirectoryName(Output)!, "field-cases.json"), overwrite: true);
        File.Copy(ManifestPath, Path.Combine(Path.GetDirectoryName(Output)!, "coverage.lock.json"), overwrite: true);
        Console.WriteLine($"Validation coverage: {manifest["rulePathCount"]} rule paths / {manifest["combinationCount"]} combinations; "
            + $"{fields["Results"]!.AsArray().Count} actual Envoy field outcomes matched Go.");
    }

    internal static void VerifyReports(JsonObject manifest, JsonArray fixtures, JsonObject catalog, JsonObject report)
    {
        foreach (string key in new[] { "originalDescriptorSha256", "derivedDescriptorSha256", "fieldRuleCount", "oneofOptionCount" })
        {
            Require(JsonNode.DeepEquals(catalog[key], manifest[key]), "Validation coverage catalog differs: " + key);
        }
        var combinations = catalog["combinations"]!.AsArray();
        Require(combinations.Count == manifest["combinationCount"]!.GetValue<int>(), "Validation coverage omitted a rule combination");
        Require(catalog["rulePathCounts"]!.AsObject().Count == manifest["rulePathCount"]!.GetValue<int>(), "Validation coverage omitted a rule path");
        var expected = new Dictionary<string, (string Field, JsonNode Case, string[] Paths)>(StringComparer.Ordinal);
        var groups = fixtures.ToDictionary(item => item!["field"]!.GetValue<string>(), StringComparer.Ordinal);
        Require(groups.Count == manifest["fieldGroupCount"]!.GetValue<int>(), "Validation coverage omitted a representative field");
        foreach (var (field, group) in groups)
        {
            foreach (var item in group!["cases"]!.AsArray())
            {
                expected.Add(field + "/" + item!["id"]!.GetValue<string>(), (field, item, Paths(group["rulePaths"]!)));
            }
        }
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var catalogFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var combination in combinations)
        {
            string[] paths = Paths(combination!["paths"]!);
            Require(signatures.Add(string.Join('+', paths)), "Validation coverage duplicated a rule combination");
            foreach (var field in combination["fields"]!.AsArray())
            {
                Require(catalogFields.Add(field!.GetValue<string>()), "Validation coverage duplicated a catalog field");
            }
            var evidence = combination["evidenceFields"]!.AsArray();
            Require(evidence.Count > 0 && evidence.All(item => groups.TryGetValue(item!.GetValue<string>(), out var group)
                && combination["fields"]!.AsArray().Any(field => JsonNode.DeepEquals(field, item))
                && Paths(group!["rulePaths"]!).SequenceEqual(paths)), "Validation combination has no matching field evidence");
        }
        Require(catalogFields.Count == manifest["fieldRuleCount"]!.GetValue<int>()
            && signatures.SetEquals(groups.Values.Select(group => string.Join('+', Paths(group!["rulePaths"]!)))),
            "Validation coverage omitted catalog fields or combinations");
        var results = report["Results"]!.AsArray();
        Require(results.Count == manifest["fieldCaseCount"]!.GetValue<int>() && results.Count == expected.Count,
            "Validation field report omitted a case");
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in results)
        {
            string id = row!["Id"]!.GetValue<string>();
            Require(observed.Add(id) && expected.ContainsKey(id), "Validation field report has an unknown or duplicate case: " + id);
            var input = expected[id];
            string field = row["Type"]!.GetValue<string>() + "." + row["Field"]!.GetValue<string>();
            bool valid = input.Case["valid"]!.GetValue<bool>();
            Require(field == input.Field && row["ExpectedValid"]!.GetValue<bool>() == valid
                && row["ActualValid"]?.GetValue<bool>() == valid && row["Error"] is null
                && (row["Violations"]!.AsArray().Count == 0) == valid
                && Paths(row["RulePaths"]!).SequenceEqual(input.Paths), "Validation field outcome differs: " + id);
        }
    }

    internal static JsonObject? ReadCurrent()
    {
        if (!File.Exists(Output))
        {
            return null;
        }
        var report = Read(Output);
        return IsCurrent(report, CurrentInputs()) ? report : null;
    }

    internal static bool IsCurrent(JsonObject report, JsonObject inputs) =>
        report["catalogWideConformance"]?.GetValue<bool>() == true && JsonNode.DeepEquals(report["inputs"], inputs);

    private static JsonObject CurrentInputs()
    {
        var inputs = new JsonObject();
        foreach (string path in new[] { ManifestPath, "schemas/validation/sources.lock.json", "schemas/validation/bindings.lock.json",
            "schemas/validation/conformance.lock.json", "schemas/validation/pgv-conformance.lock.json", "schemas/validation/rules.recognized.json",
            "Directory.Packages.props", "Directory.Build.props", "global.json" })
        {
            inputs.Add(path, GetFileSha256(path));
        }
        foreach (string root in new[] { "src/Envoy.ControlPlane.Validation", "src/Envoy.ControlPlane.Validation.Annotations",
            "tests/Envoy.ControlPlane.Validation.Tests", "tests/validation/GoOracle", "tools/Envoy.ControlPlane.SchemaTools",
            "tools/Envoy.ControlPlane.Tools/Validation" })
        {
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string normalized = path.Replace('\\', '/');
                if (normalized.Split('/').Any(part => part is "bin" or "obj") || inputs.ContainsKey(normalized)
                    || Path.GetExtension(path) is not (".cs" or ".csproj" or ".json" or ".go" or ".mod" or ".sum"))
                {
                    continue;
                }
                inputs.Add(normalized, GetFileSha256(path));
            }
        }
        inputs.Add("originalDescriptors", GetFileSha256(BindingPaths.InputsDirectory + "/catalog.pb"));
        inputs.Add("derivedDescriptors", GetFileSha256(ValidationPaths.MetadataRoot + "/catalog.pb"));
        return inputs;
    }

    private static string[] Paths(JsonNode paths) => paths.AsArray().Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
