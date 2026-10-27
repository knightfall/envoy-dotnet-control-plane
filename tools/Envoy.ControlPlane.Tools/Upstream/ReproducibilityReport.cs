using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ReproducibilityReport
{
    internal static async Task WriteAsync(string output)
    {
        var bindingLock = Read("schemas/bindings.lock.json");
        BindingGeneration.VerifyConfiguration(bindingLock);
        ValidationGeneration.VerifyOutputs();
        Require(GetFileSha256(BindingPaths.InputsDirectory + "/catalog.pb") == bindingLock["descriptorSetSha256"]!.GetValue<string>(),
            "Original descriptor set differs from lock");
        var upstream = Read("schemas/upstream.lock.json");
        SourceBaseline.Validate(Path.GetFullPath("."), upstream);
        Require(GetFileSha256("schemas/catalog.json") == upstream["catalogSha256"]!.GetValue<string>(), "Catalog differs from lock");

        string scratch = Path.GetTempFileName();
        try
        {
            await BuildProcess.RunAsync("dotnet", ["run", "--project", "tools/Envoy.ControlPlane.SchemaTools", "--configuration", "Release", "--no-restore", "--",
                "snapshot", BindingPaths.InputsDirectory + "/catalog.pb", ValidationPaths.MetadataRoot + "/catalog.pb", scratch]);
            var descriptors = Read(scratch);
            var inputs = new JsonObject();
            foreach (string path in new[] { "upstream-baseline.json", "schemas/upstream.lock.json", "schemas/catalog.json", "schemas/bindings.lock.json",
                "schemas/validation/bindings.lock.json", "schemas/validation/sources.lock.json", "schemas/validation/conformance.lock.json", "schemas/validation/rules.recognized.json", "schemas/validation/pgv-conformance.lock.json",
                "global.json", "tests/resources/envoy-images.lock.json" })
            {
                inputs.Add(path, Read(path));
            }
            var packageVersions = new JsonObject();
            foreach (var package in XDocument.Load("Directory.Packages.props").Descendants("PackageVersion"))
            {
                packageVersions.Add(package.Attribute("Include")!.Value, package.Attribute("Version")!.Value);
            }
            inputs.Add("packageVersions", packageVersions);
            foreach (string path in new[] { "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "Envoy.ControlPlane.slnx",
                "NuGet.Config", ".editorconfig", ".gitattributes" })
            {
                inputs.Add(path, File.Exists(path) ? GetFileSha256(path) : null);
            }
            foreach (string path in Directory.GetFiles(".", "buf*.yaml").Order(StringComparer.Ordinal))
            {
                inputs.Add(Path.GetFileName(path), GetFileSha256(path));
            }
            RecordMaintainedInputs(inputs, Path.GetFullPath("."));
            var coverage = new JsonObject
            {
                ["gate"] = "T26/GV incomplete",
                ["catalogWideConformance"] = false,
                ["unsupportedConstraints"] = "not-assessed",
                ["messageOneofAndDerivedConformance"] = "unproven",
                ["boundedCorpus"] = Read(ValidationPaths.SourcesLock)["corpus"]!.DeepClone(),
            };
            var rules = new JsonObject();
            foreach (var rule in descriptors["rulePathCounts"]!.AsObject())
            {
                rules.Add(rule.Key, new JsonObject { ["occurrences"] = rule.Value!.DeepClone(), ["conformance"] = "unproven" });
            }
            coverage.Add("pgvRulePaths", rules);
            Write(output, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "envoy-control-plane-reproducibility",
                ["revision"] = (await BuildProcess.RunAsync("git", ["rev-parse", "HEAD"])).Trim(),
                ["workingTreeDirty"] = (await BuildProcess.RunAsync("git", ["status", "--porcelain"])).Length > 0,
                ["inputs"] = inputs,
                ["descriptors"] = descriptors,
                ["coverage"] = coverage,
                ["verificationCatalog"] = new JsonObject
                {
                    ["goDotnetCorpora"] = JsonSerializer.SerializeToNode(RootDifferentialVerification.ModeNames()),
                    ["meaning"] = "Current corpus inventory only; no execution or passing result is implied",
                },
            });
        }
        finally
        {
            File.Delete(scratch);
        }
        Console.WriteLine("Recorded source/tool inputs, original/derived descriptors and validation rules: " + output);
    }

    internal static void Compare(string before, string after, string output) =>
        Write(output, ReproducibilityDiff.Create(Read(before), Read(after)));

    internal static void RecordMaintainedInputs(JsonObject inputs, string repository)
    {
        string[] extensions = [".cs", ".csproj", ".props", ".targets", ".proto", ".json", ".go", ".mod", ".sum", ".yaml", ".yml"];
        foreach (string directory in new[] { "src", "tests", "tools", "examples", ".github/workflows", ".config" })
        {
            string root = Path.Combine(repository, directory);
            if (!Directory.Exists(root)) continue;
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                             || file.EndsWith(".csproj.template", StringComparison.OrdinalIgnoreCase)
                             || file.EndsWith(".go.template", StringComparison.OrdinalIgnoreCase)
                             || file.EndsWith(".mod.template", StringComparison.OrdinalIgnoreCase)
                             || file.EndsWith(".sum.template", StringComparison.OrdinalIgnoreCase))
                         .Select(file => (SourcePath: file, RelativePath: Path.GetRelativePath(repository, file).Replace('\\', '/')))
                         .Where(file => !file.RelativePath.Split('/').Any(segment => segment is "bin" or "obj"))
                         .OrderBy(file => file.RelativePath, StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (inputs.ContainsKey(file.RelativePath)) continue;
                inputs.Add(file.RelativePath, ComputeSha256(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(file.SourcePath).ReplaceLineEndings("\n"))));
            }
        }
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void Write(string path, JsonObject report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
