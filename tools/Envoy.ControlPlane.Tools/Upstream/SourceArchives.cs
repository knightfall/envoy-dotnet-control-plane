using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static partial class SourceArchives
{
    internal static readonly HttpClient ArtifactHttpClient = new() { Timeout = TimeSpan.FromSeconds(180) };
    internal static readonly string[] NoticeFilePrefixes = ["LICENSE", "NOTICE", "COPYING"];

    internal static async Task FetchArtifactAsync(JsonNode artifact, string destination, bool offline)
    {
        string checksum = GetString(artifact, "sha256"), url = GetString(artifact, "url");
        Require(Sha256Pattern().IsMatch(checksum) && url.StartsWith("https://", StringComparison.Ordinal), "Invalid locked artifact");
        if (!File.Exists(destination))
        {
            Require(!offline, "Missing cached artifact: " + destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            byte[] bytes = await ArtifactHttpClient.GetByteArrayAsync(url);
            Require(ComputeSha256(bytes) == checksum, "Downloaded checksum mismatch: " + url);
            await File.WriteAllBytesAsync(destination, bytes);
        }
        Require(ComputeSha256(await File.ReadAllBytesAsync(destination)) == checksum, "Cached checksum mismatch: " + destination);
    }

    internal static Dictionary<string, byte[]> ReadArchive(string archive, JsonNode source)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var stream = File.OpenRead(archive);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
            {
                continue;
            }
            string name = entry.Name.TrimEnd('/');
            Require(!Path.IsPathRooted(name) && !name.Split('/').Contains(".."), "Unsafe archive path: " + name);
            Require(name.Split('/')[0] == GetString(source, "archivePrefix"), "Unexpected archive prefix: " + name);
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }
            string relative = name[(GetString(source, "archivePrefix").Length + 1)..];
            string filename = Path.GetFileName(relative);
            bool wanted = filename.EndsWith(".proto", StringComparison.Ordinal) || filename.EndsWith(".pb.go", StringComparison.Ordinal)
                || NoticeFilePrefixes.Any(p => filename.StartsWith(p, StringComparison.Ordinal))
                || filename is "go.mod" or "go.sum" || GetArray(source, "evidencePaths").Any(p => p!.GetValue<string>() == relative);
            if (!wanted)
            {
                continue;
            }
            using var contents = new MemoryStream();
            Require(entry.Length == 0 || entry.DataStream is not null, "Missing archive member data: " + name);
            entry.DataStream?.CopyTo(contents);
            Require(files.TryAdd(relative, contents.ToArray()), "Duplicate archive member: " + relative);
        }
        return files;
    }

    internal static async Task<Dictionary<string, Dictionary<string, byte[]>>> RetrieveAsync(JsonObject inputLock, string cache, bool offline)
    {
        var sources = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        foreach (var source in GetArray(inputLock, "sources").Select(s => s!))
        {
            string id = GetString(source, "id"), commit = GetString(source, "commit");
            Require(CommitPattern().IsMatch(commit), "Source must use a full commit: " + id);
            string repository = GetString(source, "repository");
            Require(repository.StartsWith("https://github.com/", StringComparison.Ordinal) && repository.EndsWith(".git", StringComparison.Ordinal), "Unexpected repository URL");
            string expectedUrl = "https://codeload.github.com/" + repository[19..^4] + "/tar.gz/" + commit;
            Require(GetString(source["archive"]!, "url") == expectedUrl, "Archive URL must identify the locked repository and commit");
            string archive = Path.Combine(cache, "archives", id + "-" + commit + ".tar.gz");
            await FetchArtifactAsync(source["archive"]!, archive, offline);
            var files = ReadArchive(archive, source);
            var notices = GetArray(source, "notices");
            Require(notices.Select(n => GetString(n!, "path")).ToHashSet(StringComparer.Ordinal).SetEquals(
                files.Keys.Where(p => p is "LICENSE" or "NOTICE" or "COPYING" or "LICENSE.txt")), "Incomplete root license/notice inventory: " + id);
            foreach (var notice in notices.Select(n => n!))
            {
                Require(files.TryGetValue(GetString(notice, "path"), out var data) && ComputeSha256(data) == GetString(notice, "sha256"), "License/notice checksum mismatch: " + id);
            }
            foreach (var evidence in GetArray(source, "evidence").Concat(GetArray(source, "registryPatches")).Select(n => n!))
            {
                await FetchArtifactAsync(evidence, Path.Combine(cache, "metadata", GetString(evidence, "sha256")), offline);
            }
            Require(sources.TryAdd(id, files), "Duplicate source ID: " + id);
        }
        var registry = JsonNode.Parse(sources["envoy"]["MODULE.bazel.lock"])!["registryFileHashes"]!.AsObject();
        foreach (var source in GetArray(inputLock, "sources").Select(s => s!))
        {
            var manifests = registry.Where(p => p.Key.Contains("/modules/" + GetString(source, "id") + "/", StringComparison.Ordinal)
                && p.Key.EndsWith("/source.json", StringComparison.Ordinal)).ToList();
            var expectedEvidence = new JsonArray(manifests.Select(p => (JsonNode)new JsonObject { ["url"] = p.Key, ["sha256"] = p.Value!.DeepClone() }).ToArray());
            Require(JsonNode.DeepEquals(GetArray(source, "evidence"), expectedEvidence), "Registry source evidence differs from Envoy's lock: " + GetString(source, "id"));
            foreach (var manifest in manifests)
            {
                var metadata = ReadJsonObject(Path.Combine(cache, "metadata", manifest.Value!.GetValue<string>()));
                string patchBase = manifest.Key[..manifest.Key.LastIndexOf('/')] + "/patches/";
                var expected = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var patch in metadata["patches"]?.AsObject() ?? [])
                {
                    string integrity = patch.Value!.GetValue<string>();
                    Require(integrity.StartsWith("sha256-", StringComparison.Ordinal), "Unsupported registry checksum");
                    expected.Add(patchBase + patch.Key, Convert.ToHexStringLower(Convert.FromBase64String(integrity[7..])));
                }
                var actual = GetArray(source, "registryPatches").ToDictionary(p => GetString(p!, "url"), p => GetString(p!, "sha256"), StringComparer.Ordinal);
                Require(expected.Count == actual.Count && expected.All(p => actual.GetValueOrDefault(p.Key) == p.Value), "Registry patch inventory differs from source manifest");
                foreach (var patch in GetArray(source, "registryPatches").Select(p => p!))
                {
                    string contents = File.ReadAllText(Path.Combine(cache, "metadata", GetString(patch, "sha256")));
                    string[] paths = PatchPathsPattern().Matches(contents).Select(m => m.Groups[1].Value.Split('\t')[0]).ToArray();
                    Require(JsonNode.DeepEquals(ToJsonArray(paths), patch["paths"]), "Registry patch paths differ from lock");
                    Require(!paths.Any(p => p.EndsWith(".proto", StringComparison.Ordinal)), "A schema patch needs an explicit catalog update");
                }
            }
        }
        return sources;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    internal static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    internal static partial Regex CommitPattern();

    [GeneratedRegex(@"^\+\+\+ (?:b/)?(.+)$", RegexOptions.Multiline)]
    internal static partial Regex PatchPathsPattern();
}
