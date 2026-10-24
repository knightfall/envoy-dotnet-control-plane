#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Google.Protobuf@3.35.1
#:package Roslynator.Analyzers@5.0.1

// T01 source provenance. Run from the repository root:
// dotnet run --file scripts/Upstream.cs -- sync --cache /path/outside/both/repos
// dotnet run --file scripts/Upstream.cs -- inspect --cache /path/outside/both/repos --offline
// This app does not generate C# bindings or choose the library's provider.

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.Tools;

internal static partial class Upstream
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };
    private static readonly string[] NoticeFilePrefixes = ["LICENSE", "NOTICE", "COPYING"];

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("Upstream.cs <sync|inspect> --cache <external-directory> [--offline] [--write-catalog]");
                return 0;
            }
            Require(args[0] is "sync" or "inspect", "Expected sync or inspect");
            var options = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                Require(args[i] is "--cache" or "--offline" or "--write-catalog", "Unknown argument: " + args[i]);
                string key = args[i];
                Require(!options.ContainsKey(key), "Duplicate argument: " + key);
                if (key == "--cache")
                {
                    Require(i + 1 < args.Length, "--cache requires a path");
                    options.Add(key, args[++i]);
                }
                else
                {
                    options.Add(key, null);
                }
            }
            Require(options.ContainsKey("--cache"), "--cache is required");
            Require(args[0] == "sync" || !options.ContainsKey("--write-catalog"), "--write-catalog applies to sync only");
            string root = FindRoot();
            string cache = Path.GetFullPath(options["--cache"]!);
            // Resolve existing symlinks so a cache cannot point back into a repository.
            cache = ResolveDirectory(cache);
            foreach (string repository in new[] { root, Path.Combine(Path.GetDirectoryName(root)!, "go-control-plane") })
                Require(!Within(cache, ResolveDirectory(repository)), "Use a cache outside both repositories");
            bool offline = options.ContainsKey("--offline");
            var inputLock = ReadObject(Path.Combine(root, "schemas/upstream.lock.json"));
            Require(S(ReadObject(Path.Combine(root, "global.json"))["sdk"]!, "version") ==
                    S(inputLock["inspectionTools"]!["dotnet"]!, "version"), "SDK pin differs from lock");
            var baseline = ReadObject(Path.Combine(root, "upstream-baseline.json"));
            var sourceList = A(inputLock, "sources");
            foreach (var (key, id) in new[] { ("goControlPlane", "go-control-plane"), ("envoyApi", "envoy"), ("ratelimitApi", "ratelimit") })
            {
                var source = sourceList.Single(s => S(s!, "id") == id)!;
                Require(S(baseline[key]!, "commit") == S(source, "commit") &&
                        S(baseline[key]!, "repository") == S(source, "repository"), "Baseline/lock origin mismatch: " + id);
            }
            var sources = await Retrieve(inputLock, cache, offline);
            var envoy = sourceList.Single(s => S(s!, "id") == "envoy")!;
            Require(Text(sources["go-control-plane"]["envoy/COMMIT"]).Trim() == S(envoy, "commit"), "Go's Envoy pin differs from lock");
            Require(JsonNode.DeepEquals(ModuleInventory(sources["go-control-plane"]), inputLock["goModules"]), "Go module inventory differs from lock");
            var catalog = BuildCatalog(inputLock, sources);
            string catalogPath = Path.Combine(root, "schemas/catalog.json");
            if (options.ContainsKey("--write-catalog"))
            {
                await File.WriteAllBytesAsync(catalogPath, JsonBytes(catalog));
                Console.WriteLine("Wrote schemas/catalog.json; review it and update catalogSha256 in upstream.lock.json.");
            }
            else
            {
                Require(JsonNode.DeepEquals(catalog, ReadObject(catalogPath)), "Catalog differs from pinned source inventory");
                Require(Sha(await File.ReadAllBytesAsync(catalogPath)) == S(inputLock, "catalogSha256"), "Catalog checksum differs from lock");
                Console.WriteLine("Source archives, licenses, module replacements, baseline and import closure agree.");
            }
            Console.WriteLine(catalog["counts"]!.ToJsonString(JsonOptions));
            if (args[0] == "inspect") await Inspect(inputLock, sources, catalog, cache, offline);
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "schemas/upstream.lock.json"))) return directory.FullName;
        throw new InvalidOperationException("Run from the .NET repository root or a child directory");
    }

    private static string ResolveDirectory(string path)
    {
        path = Path.GetFullPath(path);
        var parent = Directory.GetParent(path);
        if (parent is null) return path;
        string resolvedParent = ResolveDirectory(parent.FullName);
        string result = Path.Combine(resolvedParent, Path.GetFileName(path));
        if (!Directory.Exists(result)) return result;
        return new DirectoryInfo(result).ResolveLinkTarget(true)?.FullName ?? result;
    }

    private static bool Within(string path, string parent)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(parent, comparison) || path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static string S(JsonNode node, string key) => node[key]?.GetValue<string>() ?? throw new InvalidDataException("Missing lock field: " + key);
    private static JsonArray A(JsonNode node, string key) => node[key] as JsonArray ?? throw new InvalidDataException("Missing array: " + key);
    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Invalid JSON: " + path);
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
    private static byte[] JsonBytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString(JsonOptions) + "\n");
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => JsonValue.Create(v)).ToArray());

    private static async Task Fetch(JsonNode artifact, string destination, bool offline)
    {
        string checksum = S(artifact, "sha256"), url = S(artifact, "url");
        Require(Sha256Pattern().IsMatch(checksum) && url.StartsWith("https://", StringComparison.Ordinal), "Invalid locked artifact");
        if (!File.Exists(destination))
        {
            Require(!offline, "Missing cached artifact: " + destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            byte[] bytes = await Http.GetByteArrayAsync(url);
            Require(Sha(bytes) == checksum, "Downloaded checksum mismatch: " + url);
            await File.WriteAllBytesAsync(destination, bytes);
        }
        Require(Sha(await File.ReadAllBytesAsync(destination)) == checksum, "Cached checksum mismatch: " + destination);
    }

    private static Dictionary<string, byte[]> SourceFiles(string archive, JsonNode source)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var stream = File.OpenRead(archive);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType == TarEntryType.GlobalExtendedAttributes) continue;
            string name = entry.Name.TrimEnd('/');
            Require(!Path.IsPathRooted(name) && !name.Split('/').Contains(".."), "Unsafe archive path: " + name);
            Require(name.Split('/')[0] == S(source, "archivePrefix"), "Unexpected archive prefix: " + name);
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
            string relative = name[(S(source, "archivePrefix").Length + 1)..];
            string filename = Path.GetFileName(relative);
            bool wanted = filename.EndsWith(".proto", StringComparison.Ordinal) || filename.EndsWith(".pb.go", StringComparison.Ordinal)
                || NoticeFilePrefixes.Any(p => filename.StartsWith(p, StringComparison.Ordinal))
                || filename is "go.mod" or "go.sum" || A(source, "evidencePaths").Any(p => p!.GetValue<string>() == relative);
            if (!wanted) continue;
            using var contents = new MemoryStream();
            Require(entry.Length == 0 || entry.DataStream is not null, "Missing archive member data: " + name);
            entry.DataStream?.CopyTo(contents);
            Require(files.TryAdd(relative, contents.ToArray()), "Duplicate archive member: " + relative);
        }
        return files;
    }

    private static async Task<Dictionary<string, Dictionary<string, byte[]>>> Retrieve(JsonObject inputLock, string cache, bool offline)
    {
        var sources = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        foreach (var source in A(inputLock, "sources").Select(s => s!))
        {
            string id = S(source, "id"), commit = S(source, "commit");
            Require(CommitPattern().IsMatch(commit), "Source must use a full commit: " + id);
            string repository = S(source, "repository");
            Require(repository.StartsWith("https://github.com/", StringComparison.Ordinal) && repository.EndsWith(".git", StringComparison.Ordinal), "Unexpected repository URL");
            string expectedUrl = "https://codeload.github.com/" + repository[19..^4] + "/tar.gz/" + commit;
            Require(S(source["archive"]!, "url") == expectedUrl, "Archive URL must identify the locked repository and commit");
            string archive = Path.Combine(cache, "archives", id + "-" + commit + ".tar.gz");
            await Fetch(source["archive"]!, archive, offline);
            var files = SourceFiles(archive, source);
            var notices = A(source, "notices");
            Require(notices.Select(n => S(n!, "path")).ToHashSet(StringComparer.Ordinal).SetEquals(
                files.Keys.Where(p => p is "LICENSE" or "NOTICE" or "COPYING" or "LICENSE.txt")), "Incomplete root license/notice inventory: " + id);
            foreach (var notice in notices.Select(n => n!))
                Require(files.TryGetValue(S(notice, "path"), out var data) && Sha(data) == S(notice, "sha256"), "License/notice checksum mismatch: " + id);
            foreach (var evidence in A(source, "evidence").Concat(A(source, "registryPatches")).Select(n => n!))
                await Fetch(evidence, Path.Combine(cache, "metadata", S(evidence, "sha256")), offline);
            Require(sources.TryAdd(id, files), "Duplicate source ID: " + id);
        }
        var registry = JsonNode.Parse(sources["envoy"]["MODULE.bazel.lock"])!["registryFileHashes"]!.AsObject();
        foreach (var source in A(inputLock, "sources").Select(s => s!))
        {
            var manifests = registry.Where(p => p.Key.Contains("/modules/" + S(source, "id") + "/", StringComparison.Ordinal)
                && p.Key.EndsWith("/source.json", StringComparison.Ordinal)).ToList();
            var expectedEvidence = new JsonArray(manifests.Select(p => (JsonNode)new JsonObject { ["url"] = p.Key, ["sha256"] = p.Value!.DeepClone() }).ToArray());
            Require(JsonNode.DeepEquals(A(source, "evidence"), expectedEvidence), "Registry source evidence differs from Envoy's lock: " + S(source, "id"));
            foreach (var manifest in manifests)
            {
                var metadata = ReadObject(Path.Combine(cache, "metadata", manifest.Value!.GetValue<string>()));
                string patchBase = manifest.Key[..manifest.Key.LastIndexOf('/')] + "/patches/";
                var expected = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var patch in metadata["patches"]?.AsObject() ?? [])
                {
                    string integrity = patch.Value!.GetValue<string>();
                    Require(integrity.StartsWith("sha256-", StringComparison.Ordinal), "Unsupported registry checksum");
                    expected.Add(patchBase + patch.Key, Convert.ToHexStringLower(Convert.FromBase64String(integrity[7..])));
                }
                var actual = A(source, "registryPatches").ToDictionary(p => S(p!, "url"), p => S(p!, "sha256"), StringComparer.Ordinal);
                Require(expected.Count == actual.Count && expected.All(p => actual.GetValueOrDefault(p.Key) == p.Value), "Registry patch inventory differs from source manifest");
                foreach (var patch in A(source, "registryPatches").Select(p => p!))
                {
                    string contents = File.ReadAllText(Path.Combine(cache, "metadata", S(patch, "sha256")));
                    string[] paths = PatchPathsPattern().Matches(contents).Select(m => m.Groups[1].Value.Split('\t')[0]).ToArray();
                    Require(JsonNode.DeepEquals(Strings(paths), patch["paths"]), "Registry patch paths differ from lock");
                    Require(!paths.Any(p => p.EndsWith(".proto", StringComparison.Ordinal)), "A schema patch needs an explicit catalog update");
                }
            }
        }
        return sources;
    }

    private static JsonArray ModuleInventory(Dictionary<string, byte[]> files)
    {
        var result = new JsonArray();
        foreach (var (path, data) in files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) != "go.mod") continue;
            string text = Text(data);
            var blocks = new Dictionary<string, List<string>> { ["require"] = [], ["replace"] = [] };
            string? active = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Split("//", 2)[0].Trim();
                if (line is "require (" or "replace (") active = line.Split(' ')[0];
                else if (line == ")") active = null;
                else if (active is not null && line.Length > 0) blocks[active].Add(line);
                else if (line.StartsWith("require ", StringComparison.Ordinal) || line.StartsWith("replace ", StringComparison.Ordinal))
                    blocks[line.Split(' ', 2)[0]].Add(line.Split(' ', 2)[1]);
            }
            string sumPath = path[..^6] + "go.sum";
            result.Add(new JsonObject
            {
                ["path"] = path, ["sha256"] = Sha(data),
                ["module"] = ModulePattern().Match(text).Groups[1].Value,
                ["go"] = GoVersionPattern().Match(text).Groups[1].Value,
                ["requirements"] = Strings(blocks["require"]), ["replacements"] = Strings(blocks["replace"]),
                ["sumPath"] = sumPath, ["sumSha256"] = files.TryGetValue(sumPath, out var sum) ? Sha(sum) : null,
            });
        }
        return result;
    }

    private static JsonObject BuildCatalog(JsonObject inputLock, Dictionary<string, Dictionary<string, byte[]>> sources)
    {
        var lookup = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var source in A(inputLock, "sources").Select(s => s!))
        {
            foreach (string root in A(source, "protoRoots").Select(r => r!.GetValue<string>()))
            {
                string prefix = root.Length > 0 ? root + "/" : "";
                foreach (var (path, data) in sources[S(source, "id")])
                {
                    if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".proto", StringComparison.Ordinal)) continue;
                    string text = CommentTokensPattern().Replace(Text(data),
                        m => m.Value.StartsWith("//", StringComparison.Ordinal) || m.Value.StartsWith("/*", StringComparison.Ordinal) ? "" : m.Value);
                    var info = new JsonObject
                    {
                        ["source"] = S(source, "id"), ["sourcePath"] = path, ["sha256"] = Sha(data),
                        ["package"] = PackagePattern().Match(text).Groups[1].Value,
                        ["imports"] = Strings(ImportsPattern().Matches(text).Select(m => m.Groups[1].Value)),
                    };
                    Require(lookup.TryAdd(path[prefix.Length..], info), "Duplicate import owner: " + path[prefix.Length..]);
                }
            }
        }
        var mirrored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, data) in sources["go-control-plane"])
        {
            if (path.StartsWith("envoy/", StringComparison.Ordinal) && path.EndsWith(".pb.go", StringComparison.Ordinal))
            {
                var match = SourceHeaderPattern().Match(Text(data));
                if (match.Success) mirrored.Add(match.Groups[1].Value);
            }
        }
        var seeds = mirrored.Where(p => MaintainedVersionPattern().IsMatch(p) || p.StartsWith("envoy/annotations/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        seeds.UnionWith(A(inputLock["ratelimit"]!, "paths").Select(p => p!.GetValue<string>()));
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(seeds);
        while (pending.TryPop(out string? path))
        {
            if (!selected.Add(path)) continue;
            Require(lookup.ContainsKey(path), "Unresolved import: " + path);
            foreach (string dependency in A(lookup[path], "imports").Select(p => p!.GetValue<string>())) pending.Push(dependency);
        }
        var records = new JsonArray();
        foreach (var (path, info) in lookup)
        {
            if (S(info, "source") != "envoy" && !selected.Contains(path)) continue;
            string disposition, reason;
            if (seeds.Contains(path))
                (disposition, reason) = ("generate", "Selected maintained standard catalog or rate-limit configuration");
            else if (selected.Contains(path))
                (disposition, reason) = ("dependency", "Transitive import of a selected schema");
            else if (path.StartsWith("contrib/", StringComparison.Ordinal))
                (disposition, reason) = ("deferred", "T28: broad contrib catalog requires a separate scope decision");
            else if (mirrored.Contains(path))
                (disposition, reason) = ("deferred", "T28: legacy catalog requires a separate scope decision");
            else
                (disposition, reason) = ("excluded", "Outside the selected mirrored standard catalog");
            string category;
            if (path.StartsWith("contrib/", StringComparison.Ordinal)) category = "contrib";
            else if (disposition == "deferred") category = "legacy";
            else if (S(info, "source") == "ratelimit") category = "ratelimit-configuration";
            else if (path.Contains("annotations/", StringComparison.Ordinal) || path == "validate/validate.proto") category = "annotations";
            else if (S(info, "source") != "envoy") category = "external-import";
            else category = "standard";
            var record = new JsonObject { ["path"] = path };
            foreach (var entry in info) record.Add(entry.Key, entry.Value?.DeepClone());
            record.Add("category", category); record.Add("disposition", disposition); record.Add("reason", reason);
            records.Add(record);
        }
        var selectedCounts = new JsonObject();
        foreach (var group in selected.GroupBy(p => S(lookup[p], "source")).OrderBy(g => g.Key, StringComparer.Ordinal)) selectedCounts.Add(group.Key, group.Count());
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["selection"] = inputLock["selection"]!.DeepClone(),
            ["counts"] = new JsonObject { ["roots"] = seeds.Count, ["selected"] = selected.Count, ["inventory"] = records.Count, ["selectedBySource"] = selectedCounts },
            ["customExtensions"] = new JsonObject
            {
                ["selectedInputs"] = new JsonArray(),
                ["policy"] = "Application-supplied descriptors/Any payloads are supported by T05. No third-party custom schema is a release generation input until explicitly locked.",
            },
            ["files"] = records,
        };
    }

    private static async Task Inspect(JsonObject inputLock, Dictionary<string, Dictionary<string, byte[]>> sources, JsonObject catalog, string cache, bool offline)
    {
        if (!OperatingSystem.IsMacOS() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.Arm64)
            throw new InvalidOperationException("The T01 inspection compiler is pinned for macOS arm64");
        JsonNode protobuf = inputLock["inspectionTools"]!["googleProtobuf"]!;
        await Fetch(protobuf, Path.Combine(cache, "tools", new Uri(S(protobuf, "url")).Segments[^1]), offline);
        JsonNode roslynator = inputLock["inspectionTools"]!["roslynatorAnalyzers"]!;
        await Fetch(roslynator, Path.Combine(cache, "tools", new Uri(S(roslynator, "url")).Segments[^1]), offline);
        Require(Sha(await File.ReadAllBytesAsync(typeof(IMessage).Assembly.Location)) == S(protobuf, "assemblySha256"),
                "The loaded Google.Protobuf assembly differs from the locked package");
        JsonNode tool = inputLock["inspectionTools"]!["protoc"]!;
        string archive = Path.Combine(cache, "tools", new Uri(S(tool, "url")).Segments[^1]);
        await Fetch(tool, archive, offline);
        string working = Path.Combine(cache, "inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        try
        {
            ZipFile.ExtractToDirectory(archive, Path.Combine(working, "protoc"));
            string protoc = Path.Combine(working, "protoc/bin/protoc");
            File.SetUnixFileMode(protoc, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string version = await Run(protoc, ["--version"]);
            Require(version.Trim() == "libprotoc " + S(tool, "version"), "Unexpected protoc");
            var selected = A(catalog, "files").Where(f => S(f!, "disposition") is "generate" or "dependency").Select(f => f!).ToList();
            string inputs = Path.Combine(working, "inputs"), output = Path.Combine(working, "catalog.pb");
            foreach (var entry in selected)
            {
                string target = Path.Combine(inputs, S(entry, "path"));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, sources[S(entry, "source")][S(entry, "sourcePath")]);
            }
            await Run(protoc, ["-I" + inputs, "--include_imports", "--descriptor_set_out=" + output, .. selected.Select(e => S(e, "path"))]);
            var descriptors = FileDescriptorSet.Parser.ParseFrom(await File.ReadAllBytesAsync(output));
            var compiled = descriptors.File.ToDictionary(f => f.Name, StringComparer.Ordinal);
            Require(compiled.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(e => S(e, "path"))), "Descriptor closure differs from catalog");
            Console.WriteLine(version.Trim() + "; Google.Protobuf " + S(inputLock["inspectionTools"]!["googleProtobuf"]!, "version"));
            Console.WriteLine($"Compiled {compiled.Count} locked schemas with original options.");
            JsonNode ratelimit = inputLock["ratelimit"]!;
            foreach (string path in A(ratelimit, "paths").Select(p => p!.GetValue<string>()))
            {
                string generated = Text(sources["go-control-plane"][path.Replace(".proto", ".pb.go", StringComparison.Ordinal)]);
                var match = RawDescriptorPattern().Match(generated);
                Require(match.Success, "Unsupported Go raw descriptor encoding");
                byte[] raw = HexBytePattern().Matches(match.Groups[1].Value).Select(m => Convert.ToByte(m.Groups[1].Value, 16)).ToArray();
                Require(Sha(raw) == S(ratelimit["goDescriptorSha256"]!, path), "Go descriptor fingerprint differs from lock");
                var reference = FileDescriptorProto.Parser.ParseFrom(raw);
                string originalName = reference.Name;
                Require(originalName == S(ratelimit["goDescriptorNames"]!, path), "Unrecorded descriptor filename difference");
                reference.Name = path; // The sole explicitly locked normalization.
                Require(reference.Equals(compiled[path]), "Rate-limit schema definition mismatch: " + path);
                Console.WriteLine(path + ": definitions match; Go filename=" + originalName);
            }
        }
        finally { Directory.Delete(working, recursive: true); }
    }

    private static async Task<string> Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await stderr;
        Require(process.ExitCode == 0, executable + " failed: " + error);
        if (error.Length > 0) Console.Error.Write(error);
        return await stdout;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"^\+\+\+ (?:b/)?(.+)$", RegexOptions.Multiline)]
    private static partial Regex PatchPathsPattern();

    [GeneratedRegex(@"^module\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex ModulePattern();

    [GeneratedRegex(@"^go\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex GoVersionPattern();

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|/\\*.*?\\*/|//[^\\n]*", RegexOptions.Singleline)]
    private static partial Regex CommentTokensPattern();

    [GeneratedRegex(@"\bpackage\s+([\w.]+)\s*;")]
    private static partial Regex PackagePattern();

    [GeneratedRegex("\\bimport\\s+(?:(?:public|weak)\\s+)?[\"']([^\"']+)[\"']\\s*;")]
    private static partial Regex ImportsPattern();

    [GeneratedRegex("^// source: (.+)$", RegexOptions.Multiline)]
    private static partial Regex SourceHeaderPattern();

    [GeneratedRegex("/v3(?:alpha)?/")]
    private static partial Regex MaintainedVersionPattern();

    [GeneratedRegex(@"var \w+_rawDesc = \[\]byte\{(.*?)\n\}", RegexOptions.Singleline)]
    private static partial Regex RawDescriptorPattern();

    [GeneratedRegex("0x([0-9a-fA-F]{2})")]
    private static partial Regex HexBytePattern();
}
