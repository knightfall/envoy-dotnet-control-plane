using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ReleaseCandidate
{
    internal const string PolicyPath = ".github/release/policy.json";

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args is ["prepare", "--output", _, "--cache", _], "Usage: release prepare --output <new-directory> --cache <external-tool-cache>");
            string output = Path.GetFullPath(args[2]);
            string cache = Path.GetFullPath(args[4]);
            Require(!Directory.Exists(output), "Release output must be a new directory");
            string commit = (await BuildProcess.RunAsync("git", ["rev-parse", "HEAD"])).Trim();
            Require(string.IsNullOrEmpty((await BuildProcess.RunAsync("git", ["status", "--porcelain"])).Trim()),
                "Release preparation requires a clean committed checkout");
            var snapshot = Read(BindingPaths.InputsDirectory + "/results/reproducibility/snapshot.json");
            Require(snapshot["revision"]!.GetValue<string>() == commit && !snapshot["workingTreeDirty"]!.GetValue<bool>()
                && ValidationCoverage.ReadCurrent() is not null, "Run repository verify for this clean release commit first");
            PackageContentsVerification.Verify();
            var policy = Read(PolicyPath);
            string version = policy["version"]!.GetValue<string>();
            Directory.CreateDirectory(Path.Combine(output, "packages"));
            foreach (var package in policy["packages"]!.AsArray())
            {
                string id = package!.GetValue<string>();
                foreach (string extension in new[] { ".nupkg", ".snupkg" })
                {
                    string name = id + "." + version + extension;
                    string source = Path.Combine(BindingPaths.PackageDirectory, name);
                    VerifyArchive(source, id, version, commit);
                    File.Copy(source, Path.Combine(output, "packages", name));
                }
            }
            string evidence = Path.Combine(output, "evidence");
            Directory.CreateDirectory(evidence);
            foreach (string path in new[] { "upstream-baseline.json", "schemas/upstream.lock.json", "schemas/bindings.lock.json",
                "schemas/validation/sources.lock.json", "schemas/validation/bindings.lock.json", "schemas/validation/conformance.lock.json",
                "schemas/validation/pgv-conformance.lock.json", ValidationCoverage.ManifestPath, ".github/workflows/actions.lock",
                ".github/security/tools.json", "docs/releases/provenance.json", PolicyPath })
            {
                string target = Path.Combine(evidence, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(path, target);
            }
            File.Copy(BindingPaths.InputsDirectory + "/results/reproducibility/snapshot.json", Path.Combine(evidence, "verification.json"));
            File.Copy(ValidationCoverage.Output, Path.Combine(evidence, "validation-coverage.json"));
            string sourceSbom = Path.Combine(output, "source-inputs.spdx.json");
            Write(sourceSbom, SourceSbom.Create(Read("schemas/upstream.lock.json"), Read(ValidationPaths.SourcesLock), version, commit));
            string external = Path.Combine(Path.GetTempPath(), "envoy-sbom-inputs-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(external, sourceSbom + "\n");
                string tool = await PinnedReleaseTool.GetAsync("sbom-tool", cache);
                await BuildProcess.RunAsync(tool, ["generate", "-b", output, "-bc", Path.GetFullPath("."), "-pn", "Knightfall.Envoy.ControlPlane",
                    "-pv", version, "-ps", "knightfall", "-nsb", "https://github.com/knightfall/envoy-dotnet-control-plane/sbom",
                    "-mi", "SPDX:2.2", "-F", "false", "-er", external, "-pm", "true"]);
                await BuildProcess.RunAsync(tool, ["validate", "-b", output, "-mi", "SPDX:2.2", "-n", "true",
                    "-o", Path.Combine(output, "sbom-validation.json")]);
            }
            finally
            {
                File.Delete(external);
            }
            Write(Path.Combine(output, "release.json"), CreateManifest(output, version, commit));
            Console.WriteLine("Release candidate prepared with verified packages, source inputs and validated SBOM: " + output);
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static void VerifyArchive(string path, string id, string version, string commit)
    {
        using var archive = ZipFile.OpenRead(path);
        Require(archive.Entries.All(entry => !Path.IsPathRooted(entry.FullName) && !entry.FullName.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal)),
            "Unsafe package entry: " + path);
        using var manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var metadata = XDocument.Load(reader).Descendants().Single(item => item.Name.LocalName == "metadata");
        Require(Value("id") == id && Value("version") == version, "Release package identity/version differs: " + path);
        var repository = metadata.Elements().Single(item => item.Name.LocalName == "repository");
        Require((string?)repository.Attribute("commit") == commit
            && (string?)repository.Attribute("url") == "https://github.com/knightfall/envoy-dotnet-control-plane",
            "Release package source commit/repository differs: " + path);

        string Value(string name) => metadata.Elements().Single(item => item.Name.LocalName == name).Value;
    }

    internal static JsonObject CreateManifest(string directory, string version, string commit)
    {
        var files = new JsonObject();
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Release files cannot be symbolic links");
            files.Add(Path.GetRelativePath(directory, path).Replace('\\', '/'), GetFileSha256(path));
        }
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["repository"] = "knightfall/envoy-dotnet-control-plane",
            ["version"] = version, ["commit"] = commit, ["files"] = files,
        };
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static void Write(string path, JsonObject value) => File.WriteAllText(path,
        value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
