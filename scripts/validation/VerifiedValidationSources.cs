using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class VerifiedValidationSources
{
    internal static async Task<string> RetrieveAsync(string cache, JsonNode source, string destination)
    {
        string repository = Path.GetFullPath(".");
        Require(cache != repository && !cache.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Validation source cache must be outside the repository");
        Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, source["archiveName"]!.GetValue<string>());
        if (!File.Exists(archive))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            File.WriteAllBytes(archive, await client.GetByteArrayAsync(source["archiveUrl"]!.GetValue<string>()));
        }
        Require(GetFileSha256(archive) == source["archiveSha256"]!.GetValue<string>(), "Validation source archive differs from lock");

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(destination);
        await using var compressed = File.OpenRead(archive);
        await using var tar = new GZipStream(compressed, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(tar, destination, overwriteFiles: false);
        return Path.Combine(destination, source["archivePrefix"]!.GetValue<string>());
    }

    internal static async Task MaterializeEvaluatorAsync(string cache)
    {
        var runtime = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["runtime"]!;
        string source = await RetrieveAsync(cache, runtime, ValidationPaths.EvaluatorRoot + "/source");
        string code = Path.Combine(source, "src/ProtoValidate");
        const string destination = ValidationPaths.EvaluatorRoot + "/vendor";
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(destination);

        var files = runtime["csharpSources"]!.AsArray();
        Require(Directory.GetFiles(code, "*.cs", SearchOption.AllDirectories).Length == files.Count, "Evaluator source inventory differs");
        foreach (var item in files)
        {
            string relative = item!["path"]!.GetValue<string>();
            string path = Path.Combine(code, relative);
            Require(GetFileSha256(path) == item["sourceSha256"]!.GetValue<string>(), "Evaluator source differs: " + relative);
            string text = File.ReadAllText(path).TrimStart('\uFEFF').ReplaceLineEndings("\n");
            foreach (var edit in runtime["adaptations"]!.AsArray().Where(edit => edit!["path"]!.GetValue<string>() == relative))
            {
                string before = edit!["before"]!.GetValue<string>();
                Require(text.Split(before, StringSplitOptions.None).Length - 1 == edit["occurrences"]!.GetValue<int>(),
                    "Evaluator source adaptation differs: " + relative);
                text = text.Replace(before, edit["after"]!.GetValue<string>(), StringComparison.Ordinal);
            }
            text = runtime["materializedPrefix"]!.GetValue<string>() + text;
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
            Require(GetFileSha256(target) == item["adaptedSha256"]!.GetValue<string>(), "Adapted evaluator fingerprint differs: " + relative);
        }

        string license = Path.Combine(source, "LICENSE");
        Require(GetFileSha256(license) == runtime["licenseSha256"]!.GetValue<string>(), "Evaluator license differs from lock");
        File.Copy(license, ValidationPaths.EvaluatorRoot + "/LICENSE.upstream", overwrite: true);
        Directory.CreateDirectory(ValidationPaths.AnnotationsRoot + "/inputs/buf/validate");
        string annotation = Path.Combine(code, "proto/buf/validate/validate.proto");
        Require(GetFileSha256(annotation) == runtime["annotationSchemaSha256"]!.GetValue<string>(), "Annotation source differs from lock");
        File.Copy(annotation, ValidationPaths.AnnotationsRoot + "/inputs/buf/validate/validate.proto", overwrite: true);
        File.Copy(license, ValidationPaths.AnnotationsRoot + "/LICENSE.upstream", overwrite: true);
        Console.WriteLine($"Materialized {files.Count} pinned evaluator sources with reviewed adaptations.");
    }

    internal static void VerifyMaterialized()
    {
        var runtime = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["runtime"]!;
        VerifyMaterialized(ValidationPaths.EvaluatorRoot + "/vendor", runtime);
    }

    internal static void VerifyMaterialized(string directory, JsonNode runtime)
    {
        var files = runtime["csharpSources"]!.AsArray();
        Require(Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Length == files.Count, "Materialized evaluator inventory differs");
        foreach (var file in files)
        {
            string relative = file!["path"]!.GetValue<string>();
            Require(GetFileSha256(Path.Combine(directory, relative)) == file["adaptedSha256"]!.GetValue<string>(),
                "Materialized evaluator source differs: " + relative);
        }
    }
}
