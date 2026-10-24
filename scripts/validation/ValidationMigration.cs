using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

// Historical migration experiment; not included by the maintained entry point.
internal static class ValidationMigration
{
    internal static async Task RunAsync(string cache)
    {
        string repository = Path.GetFullPath(".");
        Require(cache != repository && !cache.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Source cache must be outside the repository");
        var migration = JsonNode.Parse(File.ReadAllText(ValidationAssessment.LockPath))!["migration"]!;
        string commit = migration["commit"]!.GetValue<string>();
        Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, "protovalidate-" + commit + ".tar.gz");
        if (!File.Exists(archive))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            byte[] bytes = await client.GetByteArrayAsync(migration["archiveUrl"]!.GetValue<string>());
            File.WriteAllBytes(archive, bytes);
        }
        Require(GetFileSha256(archive) == migration["archiveSha256"]!.GetValue<string>(), "Migration source archive differs from lock");

        string output = Path.GetFullPath("tests/validation/obj/migration");
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }
        Directory.CreateDirectory(output);
        await using (var compressed = File.OpenRead(archive))
        await using (var tar = new GZipStream(compressed, CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(tar, output, overwriteFiles: false);
        }

        string toolSource = Path.Combine(output, "protovalidate-" + commit, "tools");
        string tool = Path.Combine(output, OperatingSystem.IsWindows() ? "protovalidate-migrate.exe" : "protovalidate-migrate");
        await BuildProcess.RunAsync("go", ["build", "-mod=readonly", "-o", tool, "./protovalidate-migrate"], toolSource);
        string raw = Path.Combine(output, "raw");
        await BuildProcess.RunAsync(tool, ["--output", raw, Path.GetFullPath(BindingPaths.InputsDirectory + "/inputs")]);

        const string annotation = "tests/validation/ProtoValidate.Proof/bin/Release/net10.0/proto/buf/validate/validate.proto";
        Require(File.Exists(annotation), "Run Validation.cs assess before the migration proof");
        Require(GetFileSha256(annotation) == migration["annotationSchemaSha256"]!.GetValue<string>(),
            "Candidate annotation schema differs from lock");
        string destination = Path.Combine(raw, "buf/validate/validate.proto");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(annotation, destination);
        File.WriteAllText(Path.Combine(raw, "buf.yaml"), "version: v2\nmodules:\n  - path: .\n");
        var bindingLock = JsonNode.Parse(File.ReadAllText("schemas/bindings.lock.json"))!;
        string buf = await BufTool.GetExecutableAsync(cache, bindingLock["buf"]!);

        bool rejected = false;
        try
        {
            await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--exclude-source-info",
                "--output", Path.Combine(output, "unmodified.pb")], raw);
        }
        catch (InvalidOperationException error) when (error.Message == buf + " exited 100")
        {
            rejected = true;
        }
        Require(rejected, "Official migration outcome changed; reassess the locked corrections");

        MigrationCorrections.Apply(raw);
        string descriptor = Path.Combine(output, "corrected.pb");
        await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--exclude-source-info", "--output", descriptor], raw);
        Require(GetFileSha256(descriptor) == migration["correctedDescriptorSetSha256"]!.GetValue<string>(),
            "Corrected migration descriptor differs from lock");
        await BuildProcess.RunAsync("dotnet", ["run", "--project", "tests/validation/ProtoValidate.Proof/ProtoValidate.Proof.csproj",
            "--configuration", "Release", "--no-restore", "--", "compare-overlay", descriptor,
            Path.Combine(output, "comparison.json")]);
        Console.WriteLine("Corrected derived metadata preserves the original descriptors. Migrated rule execution is still UNPROVEN; G1 remains OPEN.");
    }
}
