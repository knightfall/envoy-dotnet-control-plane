using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ValidationConformance
{
    internal const string Root = "tests/Envoy.ControlPlane.Validation.Tests/obj/conformance";
    internal const string LockPath = "schemas/validation/conformance.lock.json";
    internal const string GeneratedInclude = "obj/conformance/generated/**/*.cs";

    internal static void Materialize(string runtimeSource)
    {
        var manifest = ReadManifest();
        string source = Path.Combine(runtimeSource, manifest["schemaRoot"]!.GetValue<string>());
        var schemas = manifest["schemas"]!.AsArray();
        Require(Directory.GetFiles(source, "*.proto", SearchOption.AllDirectories).Length == schemas.Count,
            "Upstream conformance schema inventory differs");
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
        foreach (var schema in schemas)
        {
            string relative = schema!["path"]!.GetValue<string>();
            CopyVerified(Path.Combine(source, relative), Path.Combine(Root, "inputs", relative), schema["sha256"]!.GetValue<string>());
        }
        var corpus = manifest["corpus"]!;
        CopyVerified(Path.Combine(runtimeSource, corpus["path"]!.GetValue<string>()), Root + "/conformance.pbbin", corpus["sha256"]!.GetValue<string>());
        var exclusions = manifest["exclusionSource"]!;
        Require(GetFileSha256(Path.Combine(runtimeSource, exclusions["path"]!.GetValue<string>())) == exclusions["sha256"]!.GetValue<string>(),
            "Upstream conformance exclusion source differs");
        File.Copy(LockPath, Root + "/conformance.lock.json");
        File.Copy(Path.Combine(runtimeSource, "LICENSE"), Root + "/LICENSE.upstream");
    }

    internal static async Task GenerateAsync(string buf)
    {
        var manifest = ReadManifest();
        foreach (var import in manifest["imports"]!.AsArray())
        {
            string relative = import!["path"]!.GetValue<string>();
            CopyVerified(Path.Combine(ValidationPaths.AnnotationsRoot, "inputs", relative),
                Path.Combine(Root, "inputs", relative), import["sha256"]!.GetValue<string>());
        }
        File.WriteAllText(Root + "/inputs/buf.yaml", "version: v2\nmodules:\n  - path: .\n");
        await BuildProcess.RunAsync(buf, ["generate", Root + "/inputs", "--template", "buf.validation-conformance.gen.yaml",
            "--path", Root + "/inputs/buf/validate/conformance"]);
        VerifyOutputs();
    }

    internal static void VerifyOutputs()
    {
        var manifest = ReadManifest();
        foreach (var (path, hash) in manifest["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == hash!.GetValue<string>(), "Conformance generation configuration differs: " + path);
        }
        var inputs = manifest["schemas"]!.AsArray().Concat(manifest["imports"]!.AsArray()).ToArray();
        Require(Directory.GetFiles(Root + "/inputs", "*.proto", SearchOption.AllDirectories).Length == inputs.Length,
            "Materialized conformance input inventory differs");
        foreach (var input in inputs)
        {
            string path = Path.Combine(Root, "inputs", input!["path"]!.GetValue<string>());
            Require(GetFileSha256(path) == input["sha256"]!.GetValue<string>(), "Materialized conformance input differs: " + path);
        }
        Require(GetFileSha256(Root + "/conformance.pbbin") == manifest["corpus"]!["sha256"]!.GetValue<string>(),
            "Upstream conformance cases differ from lock");
        Require(GetFileSha256(Root + "/conformance.lock.json") == GetFileSha256(LockPath), "Materialized conformance manifest differs");
        VerifyGeneratedSources(Root + "/generated", manifest["generatedSourceCount"]!.GetValue<int>(),
            manifest["generatedSourcesSha256"]!.GetValue<string>());
    }

    internal static void VerifyReport()
    {
        var manifest = ReadManifest();
        const string path = "tests/Envoy.ControlPlane.Validation.Tests/bin/Release/net10.0/validation-conformance.json";
        var report = JsonNode.Parse(File.ReadAllText(path))!;
        var cases = report["Cases"]!.AsArray();
        Require(report["SourceCommit"]!.GetValue<string>() == manifest["sourceCommit"]!.GetValue<string>()
            && report["CorpusSha256"]!.GetValue<string>() == manifest["corpus"]!["sha256"]!.GetValue<string>(),
            "Conformance report source differs from lock");
        Require(cases.Count == manifest["caseCount"]!.GetValue<int>()
            && report["Suites"]!.GetValue<int>() == manifest["suiteCount"]!.GetValue<int>(), "Conformance report omitted cases or suites");
        var excluded = manifest["exclusions"]!.AsArray().Select(item =>
            item!["suite"]!.GetValue<string>() + "/" + item["case"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            string id = item!["Suite"]!.GetValue<string>() + "/" + item["Case"]!.GetValue<string>();
            Require(observed.Add(id), "Conformance report duplicated a case: " + id);
            Require(item["Status"]!.GetValue<string>() == (excluded.Contains(id) ? "excluded" : "passed"),
                "Conformance report contains a failure or undeclared exclusion: " + id);
            if (!excluded.Contains(id))
            {
                Require(item["Expected"]!.GetValue<string>() == item["Actual"]!.GetValue<string>()
                    && item["ExpectedDiagnostics"] is JsonArray && item["ActualDiagnostics"] is JsonArray
                    && JsonNode.DeepEquals(item["ExpectedDiagnostics"], item["ActualDiagnostics"]),
                    "Conformance report marked a mismatched result as passed: " + id);
            }
        }
        Require(excluded.IsSubsetOf(observed), "Conformance report omitted a declared exclusion");
        const string destination = BindingPaths.InputsDirectory + "/results/validation/conformance.json";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(path, destination, overwrite: true);
        File.Copy(LockPath, Path.Combine(Path.GetDirectoryName(destination)!, "conformance.lock.json"), overwrite: true);
        File.Copy(Root + "/LICENSE.upstream", Path.Combine(Path.GetDirectoryName(destination)!, "LICENSE.conformance"), overwrite: true);
        Console.WriteLine($"Pinned conformance: {cases.Count - excluded.Count} passed; {excluded.Count} declared upstream clock exclusions.");
    }

    internal static void CopyToConsumer(string destination)
    {
        VerifyOutputs();
        const string generated = Root + "/generated";
        foreach (string source in Directory.EnumerateFiles(generated, "*.cs", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, "Conformance", Path.GetRelativePath(generated, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        string resources = Path.Combine(destination, "obj/conformance");
        Directory.CreateDirectory(resources);
        foreach (string file in new[] { "conformance.pbbin", "conformance.lock.json" })
        {
            File.Copy(Path.Combine(Root, file), Path.Combine(resources, file));
        }
    }

    private static JsonNode ReadManifest()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(LockPath))!;
        var runtime = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["runtime"]!;
        Require(manifest["sourceCommit"]!.GetValue<string>() == runtime["commit"]!.GetValue<string>(),
            "Conformance source differs from the pinned evaluator");
        return manifest;
    }

    private static void CopyVerified(string source, string destination, string hash)
    {
        Require(GetFileSha256(source) == hash, "Conformance source differs: " + source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }
}
