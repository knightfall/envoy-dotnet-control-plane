using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Upstream.Tests;

internal sealed class UpstreamFixture : IDisposable
{
    internal const string RootProto = "envoy/config/core/v3/base.proto";
    internal const string DependencyProto = "xds/type/v1/extension.proto";
    private const string RatelimitProto = "ratelimit/config/ratelimit/v3/rls_conf.proto";
    private const string LegacyProto = "envoy/api/v2/legacy.proto";
    private const string ContribProto = "contrib/envoy/extensions/example/v3/example.proto";
    private const string ExcludedProto = "bazel/testdata/example.proto";
    private const string Module = "module github.com/envoyproxy/go-control-plane\n\ngo 1.26.0\n\nrequire example.test/import v1.0.0\n\nreplace example.test/import => ./envoy\n";
    private const string ModuleSum = "example.test/import v1.0.0 h1:fixture\n";
    private const string License = "Synthetic test fixture license.\n";
    private const string PatchUrl = "https://fixture.invalid/modules/xds/1.0/patches/build.patch";
    private const string ManifestUrl = "https://fixture.invalid/modules/xds/1.0/source.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "envoy-upstream-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, Dictionary<string, byte[]>> sourceFiles = new(StringComparer.Ordinal);

    internal string Root => Path.Combine(temporary, "repository");
    internal string Cache => Path.Combine(temporary, "cache");
    internal string CatalogPath => Path.Combine(Root, "schemas/catalog.json");
    internal JsonObject InputLock { get; }
    internal JsonObject Catalog { get; }

    internal UpstreamFixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "schemas"));
        Directory.CreateDirectory(Path.Combine(Cache, "metadata"));
        var patch = Bytes("--- a/BUILD.bazel\n+++ b/BUILD.bazel\n@@ -1 +1 @@\n-old\n+new\n");
        var manifest = new JsonObject
        {
            ["patches"] = new JsonObject { ["build.patch"] = "sha256-" + Convert.ToBase64String(SHA256.HashData(patch)) },
        };
        byte[] manifestBytes = JsonBytes(manifest);
        File.WriteAllBytes(Path.Combine(Cache, "metadata", Sha(patch)), patch);
        File.WriteAllBytes(Path.Combine(Cache, "metadata", Sha(manifestBytes)), manifestBytes);

        sourceFiles.Add("go-control-plane", new(StringComparer.Ordinal)
        {
            ["LICENSE"] = Bytes(License), ["go.mod"] = Bytes(Module), ["go.sum"] = Bytes(ModuleSum),
            ["envoy/COMMIT"] = Bytes(new string('2', 40) + "\n"),
            ["envoy/config/core/v3/base.pb.go"] = Bytes("// source: " + RootProto + "\n"),
            ["envoy/api/v2/legacy.pb.go"] = Bytes("// source: " + LegacyProto + "\n"),
        });
        sourceFiles.Add("envoy", new(StringComparer.Ordinal)
        {
            ["LICENSE"] = Bytes(License),
            ["MODULE.bazel.lock"] = JsonBytes(new JsonObject
            {
                ["registryFileHashes"] = new JsonObject { [ManifestUrl] = Sha(manifestBytes) },
            }),
            ["api/" + RootProto] = Bytes("syntax = \"proto3\";\npackage envoy.config.core.v3;\nimport \"" + DependencyProto + "\";\nmessage Node { xds.type.v1.Extension extension = 1; }\n"),
            ["api/" + LegacyProto] = Bytes("syntax = \"proto3\";\npackage envoy.api.v2;\nmessage Legacy {}\n"),
            ["api/" + ContribProto] = Bytes("syntax = \"proto3\";\npackage envoy.extensions.example.v3;\nmessage Example {}\n"),
            ["api/" + ExcludedProto] = Bytes("syntax = \"proto3\";\npackage testdata;\nmessage Example {}\n"),
        });
        sourceFiles.Add("ratelimit", new(StringComparer.Ordinal)
        {
            ["LICENSE"] = Bytes(License),
            ["api/" + RatelimitProto] = Bytes("syntax = \"proto3\";\npackage ratelimit.config.ratelimit.v3;\nmessage RateLimitConfig { string name = 1; }\n"),
        });
        sourceFiles.Add("xds", new(StringComparer.Ordinal)
        {
            ["LICENSE"] = Bytes(License),
            [DependencyProto] = Bytes("syntax = \"proto3\";\npackage xds.type.v1;\nmessage Extension { string name = 1; }\n"),
        });

        var sources = new JsonArray(
            SourceSpec("go-control-plane", '1', [], ["envoy/COMMIT"]),
            SourceSpec("envoy", '2', ["api"], ["MODULE.bazel.lock"]),
            SourceSpec("ratelimit", '3', ["api"], []),
            SourceSpec("xds", '4', [""], []));
        sources[3]!["evidence"] = new JsonArray(new JsonObject { ["url"] = ManifestUrl, ["sha256"] = Sha(manifestBytes) });
        sources[3]!["registryPatches"] = new JsonArray(new JsonObject
        {
            ["url"] = PatchUrl, ["sha256"] = Sha(patch), ["paths"] = new JsonArray("BUILD.bazel"),
        });
        InputLock = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["sources"] = sources,
            ["selection"] = new JsonObject { ["id"] = "synthetic-offline-fixture" },
            ["goModules"] = new JsonArray(new JsonObject
            {
                ["path"] = "go.mod", ["sha256"] = Sha(Bytes(Module)),
                ["module"] = "github.com/envoyproxy/go-control-plane", ["go"] = "1.26.0",
                ["requirements"] = new JsonArray("example.test/import v1.0.0"),
                ["replacements"] = new JsonArray("example.test/import => ./envoy"),
                ["sumPath"] = "go.sum", ["sumSha256"] = Sha(Bytes(ModuleSum)),
            }),
            ["ratelimit"] = new JsonObject { ["paths"] = new JsonArray(RatelimitProto) },
            ["inspectionTools"] = new JsonObject { ["dotnet"] = new JsonObject { ["version"] = "10.0.400" } },
        };
        foreach (var source in sources.Select(s => s!)) WriteArchive(source["id"]!.GetValue<string>());
        WriteJson(Path.Combine(Root, "global.json"), new JsonObject { ["sdk"] = new JsonObject { ["version"] = "10.0.400" } });
        var baseline = new JsonObject();
        foreach (var (key, id) in new[] { ("goControlPlane", "go-control-plane"), ("envoyApi", "envoy"), ("ratelimitApi", "ratelimit") })
        {
            var source = Source(id);
            baseline.Add(key, new JsonObject { ["repository"] = source["repository"]!.DeepClone(), ["commit"] = source["commit"]!.DeepClone() });
        }
        WriteJson(Path.Combine(Root, "upstream-baseline.json"), baseline);

        // The expected six-file catalog is explicit, independent of the CLI's
        // catalog builder and --write-catalog update command.
        Catalog = new JsonObject
        {
            ["schemaVersion"] = 1, ["selection"] = InputLock["selection"]!.DeepClone(),
            ["counts"] = new JsonObject
            {
                ["roots"] = 2, ["selected"] = 3, ["inventory"] = 6,
                ["selectedBySource"] = new JsonObject { ["envoy"] = 1, ["ratelimit"] = 1, ["xds"] = 1 },
            },
            ["customExtensions"] = new JsonObject
            {
                ["selectedInputs"] = new JsonArray(),
                ["policy"] = "Application-supplied descriptors/Any payloads are supported by T05. No third-party custom schema is a release generation input until explicitly locked.",
            },
            ["files"] = new JsonArray(
                Entry(ExcludedProto, "envoy", "api/", "testdata", [], "standard", "excluded", "Outside the selected mirrored standard catalog"),
                Entry(ContribProto, "envoy", "api/", "envoy.extensions.example.v3", [], "contrib", "deferred", "T28: broad contrib catalog requires a separate scope decision"),
                Entry(LegacyProto, "envoy", "api/", "envoy.api.v2", [], "legacy", "deferred", "T28: legacy catalog requires a separate scope decision"),
                Entry(RootProto, "envoy", "api/", "envoy.config.core.v3", [DependencyProto], "standard", "generate", "Selected maintained standard catalog or rate-limit configuration"),
                Entry(RatelimitProto, "ratelimit", "api/", "ratelimit.config.ratelimit.v3", [], "ratelimit-configuration", "generate", "Selected maintained standard catalog or rate-limit configuration"),
                Entry(DependencyProto, "xds", "", "xds.type.v1", [], "external-import", "dependency", "Transitive import of a selected schema")),
        };
        SaveCatalog();
    }

    internal JsonObject Source(string id) => InputLock["sources"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!.AsObject();

    internal string ArchivePath(string id)
    {
        var source = Source(id);
        return Path.Combine(Cache, "archives", id + "-" + source["commit"]!.GetValue<string>() + ".tar.gz");
    }

    internal void SaveLock() => WriteJson(Path.Combine(Root, "schemas/upstream.lock.json"), InputLock);

    internal void SaveCatalog()
    {
        WriteJson(CatalogPath, Catalog);
        InputLock["catalogSha256"] = Sha(File.ReadAllBytes(CatalogPath));
        SaveLock();
    }

    internal void ReplaceSourceFile(string id, string path, string contents)
    {
        sourceFiles[id][path] = Bytes(contents);
        WriteArchive(id);
        SaveLock();
    }

    internal string InputFingerprint() => string.Join('\n', Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(temporary, p) + ":" + Sha(File.ReadAllBytes(p))));

    internal async Task<CommandResult> Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "upstream/Upstream.dll"));
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the upstream CLI");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The offline fixture CLI did not terminate");
        }
        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    internal Task<CommandResult> Sync() => Run("sync", "--cache", Cache, "--offline");

    public void Dispose() => Directory.Delete(temporary, recursive: true);

    private static JsonObject SourceSpec(string id, char commit, string[] roots, string[] evidencePaths)
    {
        string revision = new(commit, 40);
        return new JsonObject
        {
            ["id"] = id, ["commit"] = revision, ["repository"] = "https://github.com/fixture/" + id + ".git",
            ["archivePrefix"] = id + "-" + revision,
            ["archive"] = new JsonObject { ["url"] = "https://codeload.github.com/fixture/" + id + "/tar.gz/" + revision },
            ["protoRoots"] = new JsonArray(roots.Select(r => JsonValue.Create(r)).ToArray()),
            ["evidencePaths"] = new JsonArray(evidencePaths.Select(p => JsonValue.Create(p)).ToArray()),
            ["notices"] = new JsonArray(new JsonObject { ["path"] = "LICENSE", ["sha256"] = Sha(Bytes(License)) }),
            ["evidence"] = new JsonArray(), ["registryPatches"] = new JsonArray(),
        };
    }

    private JsonObject Entry(string path, string source, string prefix, string package, string[] imports, string category, string disposition, string reason) => new()
    {
        ["path"] = path, ["source"] = source, ["sourcePath"] = prefix + path,
        ["sha256"] = Sha(sourceFiles[source][prefix + path]), ["package"] = package,
        ["imports"] = new JsonArray(imports.Select(i => JsonValue.Create(i)).ToArray()),
        ["category"] = category, ["disposition"] = disposition, ["reason"] = reason,
    };

    private void WriteArchive(string id)
    {
        var source = Source(id);
        string path = ArchivePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var output = File.Create(path))
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        using (var writer = new TarWriter(gzip))
        {
            foreach (var (name, data) in sourceFiles[id].OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                using var contents = new MemoryStream(data);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, source["archivePrefix"]!.GetValue<string>() + "/" + name)
                {
                    DataStream = contents, ModificationTime = DateTimeOffset.UnixEpoch,
                };
                writer.WriteEntry(entry);
            }
        }
        source["archive"]!["sha256"] = Sha(File.ReadAllBytes(path));
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] JsonBytes(JsonNode value) => Bytes(value.ToJsonString(JsonOptions) + "\n");
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void WriteJson(string path, JsonNode value) => File.WriteAllBytes(path, JsonBytes(value));
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
