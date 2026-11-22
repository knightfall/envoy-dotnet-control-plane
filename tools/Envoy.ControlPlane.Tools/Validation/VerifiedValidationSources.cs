using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class VerifiedValidationSources
{
    internal static Task<string> RetrieveAsync(string cache, JsonNode source, string destination) =>
        RetrieveAsync(cache, source, destination, DownloadArchiveAsync);

    internal static async Task<string> RetrieveAsync(string cache, JsonNode source, string destination,
        Func<Uri, Task<byte[]>> download)
    {
        string repository = RepositoryPaths.ResolveDirectoryLinks(".");
        cache = RepositoryPaths.ResolveDirectoryLinks(cache);
        Require(!RepositoryPaths.IsWithinDirectory(cache, repository), "Validation source cache must be outside the repository");

        string url = source["archiveUrl"]!.GetValue<string>();
        Require(Uri.TryCreate(url, UriKind.Absolute, out var archiveUri) && archiveUri.Scheme == Uri.UriSchemeHttps,
            "Validation source archive URL must use HTTPS");

        Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, source["archiveName"]!.GetValue<string>());
        string expected = source["archiveSha256"]!.GetValue<string>();
        byte[]? bytes = File.Exists(archive) ? await File.ReadAllBytesAsync(archive) : null;
        if (bytes is null || ComputeSha256(bytes) != expected)
        {
            bytes = await download(archiveUri!);
            Require(ComputeSha256(bytes) == expected, "Validation source archive differs from lock");
            string temporary = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, archive, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(destination);
        await using var compressed = new MemoryStream(bytes, writable: false);
        await using var tar = new GZipStream(compressed, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(tar, destination, overwriteFiles: false);
        return Path.Combine(destination, source["archivePrefix"]!.GetValue<string>());
    }

    private static async Task<byte[]> DownloadArchiveAsync(Uri uri)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        return await client.GetByteArrayAsync(uri);
    }

    internal static async Task<string> MaterializeEvaluatorAsync(string cache)
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
        return source;
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
