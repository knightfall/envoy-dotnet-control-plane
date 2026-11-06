using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PgvConformance
{
    internal const string Root = "tests/Envoy.ControlPlane.Validation.Tests/obj/pgv-conformance";
    internal const string LockPath = "schemas/validation/pgv-conformance.lock.json";
    internal const string GeneratedInclude = "obj/pgv-conformance/generated/**/*.cs";

    internal static async Task GenerateAsync(string cache, string buf, string migrationTool)
    {
        var manifest = ReadManifest();
        foreach (var (path, expected) in manifest["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == expected!.GetValue<string>(), "PGV fixture configuration differs: " + path);
        }
        var upstream = JsonNode.Parse(File.ReadAllText("schemas/upstream.lock.json"))!["sources"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<string>() == manifest["sourceId"]!.GetValue<string>())!.DeepClone();
        Require(upstream["commit"]!.GetValue<string>() == manifest["sourceCommit"]!.GetValue<string>(), "PGV fixture source pin differs");
        string archive = Path.Combine(cache, "archives", upstream["id"]!.GetValue<string>() + "-" + upstream["commit"]!.GetValue<string>() + ".tar.gz");
        await SourceArchives.FetchArtifactAsync(upstream["archive"]!, archive, offline: false);
        upstream["evidencePaths"]!.AsArray().Add(manifest["executorPath"]!.GetValue<string>());
        var sourceFiles = SourceArchives.ReadArchive(archive, upstream);

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
        const string inputs = Root + "/inputs";
        foreach (var item in manifest["schemas"]!.AsArray())
        {
            string path = item!["path"]!.GetValue<string>();
            byte[] bytes = sourceFiles[path];
            Require(ComputeSha256(bytes) == item["sha256"]!.GetValue<string>(), "PGV fixture schema differs: " + path);
            WriteBytes(Path.Combine(inputs, path), bytes);
        }
        Require(sourceFiles.Keys.Count(path => path.StartsWith("tests/harness/cases/", StringComparison.Ordinal)
            && path.EndsWith(".proto", StringComparison.Ordinal)) == manifest["schemas"]!.AsArray().Count, "PGV fixture schema inventory differs");
        string executor = manifest["executorPath"]!.GetValue<string>();
        Require(ComputeSha256(sourceFiles[executor]) == manifest["executorSha256"]!.GetValue<string>(), "PGV fixture cases differ");
        WriteBytes(Root + "/go/cases.go", sourceFiles[executor]);
        WriteBytes(Root + "/LICENSE.upstream", sourceFiles["LICENSE"]);
        CopyImport(BindingPaths.InputsDirectory + "/inputs/validate/validate.proto", inputs + "/validate/validate.proto");
        foreach (string file in Directory.GetFiles(BindingPaths.InputsDirectory + "/inputs/google/protobuf", "*.proto"))
        {
            CopyImport(file, Path.Combine(inputs, "google/protobuf", Path.GetFileName(file)));
        }
        File.WriteAllText(inputs + "/buf.yaml", "version: v2\nmodules:\n  - path: .\n");
        await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--exclude-source-info", "--output", Root + "/original.pb", inputs]);
        await BuildProcess.RunAsync(buf, ["generate", inputs, "--template", "buf.pgv-go.gen.yaml", "--path", inputs + "/tests/harness/cases"]);
        foreach (string name in new[] { "main.go", "go.mod", "go.sum" })
        {
            File.Copy("tests/validation/PgvConformance/" + name + ".template", Root + "/go/" + name);
        }
        VerifyGeneratedGo(manifest);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", Path.GetFullPath(Root + "/cases.json")], Root + "/go");

        string derived = Path.GetFullPath(Root + "/derived-inputs");
        await BuildProcess.RunAsync(migrationTool, ["--output", derived, Path.GetFullPath(inputs)]);
        CopyImport(ValidationPaths.AnnotationsRoot + "/inputs/buf/validate/validate.proto", Path.Combine(derived, "buf/validate/validate.proto"));
        File.WriteAllText(Path.Combine(derived, "buf.yaml"), "version: v2\nmodules:\n  - path: .\n");
        MigrationCorrections.Apply(derived);
        RemoveOutOfScopeDisabledOptions(Path.Combine(derived, "tests/harness/cases/messages.proto"));
        const string migrated = Root + "/migrated.pb";
        await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--output", migrated, derived]);
        await BuildProcess.RunAsync("dotnet", ["run", "--project", "tools/Envoy.ControlPlane.SchemaTools", "--configuration", "Release", "--no-restore", "--",
            "overlay", Root + "/original.pb", migrated, Root + "/catalog.pb"]);
        await BuildProcess.RunAsync(buf, ["generate", Root + "/catalog.generation.pb#format=bin", "--template", "buf.pgv-csharp.gen.yaml",
            "--exclude-path", "google/protobuf", "--exclude-path", "buf/validate", "--exclude-path", "validate"]);
        File.Copy(LockPath, Root + "/pgv-conformance.lock.json");
        File.Copy("schemas/validation/rules.recognized.json", Root + "/rules.recognized.json");
        File.WriteAllText(Root + "/cases.sha256", GetFileSha256(Root + "/cases.json") + "\n");
        VerifyOutputs();
    }

    internal static void VerifyOutputs()
    {
        var manifest = ReadManifest();
        foreach (var (path, expected) in manifest["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == expected!.GetValue<string>(), "PGV fixture configuration differs: " + path);
        }
        Require(GetFileSha256(Root + "/pgv-conformance.lock.json") == GetFileSha256(LockPath), "PGV fixture lock differs");
        Require(GetFileSha256(Root + "/rules.recognized.json") == GetFileSha256("schemas/validation/rules.recognized.json"), "PGV fixture rule selection differs");
        Require(GetFileSha256(Root + "/go/cases.go") == manifest["executorSha256"]!.GetValue<string>(), "PGV fixture cases differ");
        VerifyGeneratedGo(manifest);
        VerifyGeneratedSources(Root + "/generated", manifest["generatedCsharpCount"]!.GetValue<int>(), manifest["generatedCsharpSha256"]!.GetValue<string>());
        var cases = JsonNode.Parse(File.ReadAllText(Root + "/cases.json"))!.AsArray();
        Require(cases.Count == manifest["goCaseCount"]!.GetValue<int>(), "PGV fixture Go result inventory differs");
        for (int index = 0; index < cases.Count; index++)
        {
            Require(cases[index]!["id"]!.GetValue<string>() == index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                "PGV fixture Go result order differs");
        }
        Require(File.ReadAllText(Root + "/cases.sha256").Trim() == GetFileSha256(Root + "/cases.json"),
            "PGV fixture Go results changed after generation");
    }

    internal static void VerifyReport()
    {
        var manifest = ReadManifest();
        const string path = "tests/Envoy.ControlPlane.Validation.Tests/bin/Release/net10.0/validation-pgv-conformance.json";
        var report = JsonNode.Parse(File.ReadAllText(path))!;
        var cases = report["Cases"]!.AsArray();
        var expected = JsonNode.Parse(File.ReadAllText(Root + "/cases.json"))!.AsArray()
            .ToDictionary(item => item!["id"]!.GetValue<string>(), StringComparer.Ordinal);
        Require(report["SourceCommit"]!.GetValue<string>() == manifest["sourceCommit"]!.GetValue<string>()
            && cases.Count == manifest["goCaseCount"]!.GetValue<int>(), "PGV report source or count differs");
        Require(report["ExpectedSelected"]!.GetValue<int>() == manifest["selectedCaseCount"]!.GetValue<int>()
            && report["ExpectedSelectionHash"]!.GetValue<string>() == manifest["selectedIdsSha256"]!.GetValue<string>()
            && report["ActualSelectionHash"]!.GetValue<string>() == manifest["selectedIdsSha256"]!.GetValue<string>(),
            "PGV report selection differs from lock");
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<string>();
        foreach (var item in cases)
        {
            string id = item!["Id"]!.GetValue<string>();
            Require(observed.Add(id) && expected.TryGetValue(id, out _), "PGV report contains an unknown or duplicate case: " + id);
            var source = expected[id]!;
            Require(item["Name"]!.GetValue<string>() == source["name"]!.GetValue<string>()
                && item["Type"]!.GetValue<string>() == source["type"]!.GetValue<string>()
                && item["ExpectedValid"]!.GetValue<bool>() == source["expectedValid"]!.GetValue<bool>(),
                "PGV report changed the Go outcome: " + id);
            string status = item["Status"]!.GetValue<string>();
            var unsupported = item["UnsupportedRules"]!.AsArray();
            if (status == "excluded")
            {
                Require(unsupported.Count > 0 && item["ActualValid"] is null, "PGV report has an unexplained exclusion: " + id);
            }
            else
            {
                Require(status == "passed" && unsupported.Count == 0 && item["Error"] is null
                    && item["ActualValid"]!.GetValue<bool>() == source["expectedValid"]!.GetValue<bool>(),
                    "PGV report has a failed or inconsistent case: " + id);
                selected.Add(id);
            }
        }
        string selection = string.Join('\n', selected.Order(StringComparer.Ordinal));
        Require(selected.Count == manifest["selectedCaseCount"]!.GetValue<int>()
            && ComputeSha256(System.Text.Encoding.UTF8.GetBytes(selection)) == manifest["selectedIdsSha256"]!.GetValue<string>(),
            "PGV report selected case inventory differs");
        const string destination = BindingPaths.InputsDirectory + "/results/validation";
        Directory.CreateDirectory(destination);
        File.Copy(path, Path.Combine(destination, "pgv-conformance.json"), overwrite: true);
        File.Copy(LockPath, Path.Combine(destination, "pgv-conformance.lock.json"), overwrite: true);
        File.Copy(Root + "/LICENSE.upstream", Path.Combine(destination, "LICENSE.pgv"), overwrite: true);
        Console.WriteLine($"Pinned PGV fixtures: {selected.Count} passed; {cases.Count - selected.Count} outside selected rule scope.");
    }

    internal static void CopyToConsumer(string destination)
    {
        VerifyOutputs();
        foreach (string source in Directory.EnumerateFiles(Root + "/generated", "*.cs", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, "PgvConformance", Path.GetRelativePath(Root + "/generated", source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        string resources = Path.Combine(destination, "obj/pgv-conformance");
        Directory.CreateDirectory(resources);
        foreach (string file in new[] { "cases.json", "pgv-conformance.lock.json", "rules.recognized.json" })
        {
            File.Copy(Path.Combine(Root, file), Path.Combine(resources, file));
        }
    }

    private static JsonNode ReadManifest() => JsonNode.Parse(File.ReadAllText(LockPath))!;

    private static void VerifyGeneratedGo(JsonNode manifest)
    {
        const string root = Root + "/go";
        string[] files = Directory.GetFiles(root + "/harness", "*.go", SearchOption.AllDirectories);
        Require(files.Length == manifest["generatedGoCount"]!.GetValue<int>(), "PGV fixture Go file count differs");
        string hash = GetGeneratedSourcesSha256(files.Select(path => (Path.GetRelativePath(root, path), File.ReadAllText(path))));
        Require(hash == manifest["generatedGoSha256"]!.GetValue<string>(), "PGV fixture Go fingerprint differs");
    }

    private static void RemoveOutOfScopeDisabledOptions(string path)
    {
        const string option = "option (buf.validate.message).disabled = true;";
        string source = File.ReadAllText(path);
        Require(source.Split(option, StringSplitOptions.None).Length - 1 == 2, "PGV fixture disabled-option inventory differs");
        File.WriteAllText(path, source.Replace(option, "", StringComparison.Ordinal));
    }

    private static void CopyImport(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
