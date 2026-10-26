using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class EnvoyVerification
{
    private const string Project = "tests/Envoy.ControlPlane.EnvoyProbe/Envoy.ControlPlane.EnvoyProbe.csproj";
    private const string ImageLock = "tests/resources/envoy-images.lock.json";
    private static readonly Regex EnvoyImagePattern = new("^envoyproxy/envoy@sha256:[0-9a-f]{64}$");
    private static readonly Regex SdkImagePattern = new("^mcr.microsoft.com/dotnet/sdk@sha256:[0-9a-f]{64}$");

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args is ["verify"], "Usage: envoy verify (requires Docker and generated bindings)");
            Require(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Envoy integration supports Linux/macOS Docker clients");
            Environment.CurrentDirectory = RepositoryPaths.FindRepositoryRoot();
            await VerifyAsync();
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task VerifyAsync()
    {
        var pins = JsonNode.Parse(File.ReadAllText(ImageLock))!;
        var sdk = JsonNode.Parse(File.ReadAllText("global.json"))!;
        Require(pins["sdkVersion"]!.GetValue<string>() == sdk["sdk"]!["version"]!.GetValue<string>(), "Envoy SDK image pin differs from global.json");
        string engine = (await BuildProcess.RunAsync("docker", ["info", "--format", "{{.OSType}}/{{.Architecture}}"])).Trim();
        string platform = engine switch
        {
            "linux/amd64" or "linux/x86_64" => "linux/amd64",
            "linux/arm64" or "linux/aarch64" => "linux/arm64",
            _ => throw new InvalidOperationException("Unsupported Docker engine: " + engine),
        };
        var selected = pins["platforms"]![platform]!;
        string envoyImage = selected["envoyImage"]!.GetValue<string>();
        string sdkImage = selected["sdkImage"]!.GetValue<string>();
        Require(EnvoyImagePattern.IsMatch(envoyImage) && SdkImagePattern.IsMatch(sdkImage),
            "Envoy integration images must use immutable official repository digests");
        string output = Path.GetFullPath(Path.Combine(BindingPaths.InputsDirectory, "results", "envoy"));
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        string scratch = Path.Combine(Path.GetTempPath(), "envoy-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string sourceContainer = "envoy-source-" + Guid.NewGuid().ToString("N");
        try
        {
            await BuildProcess.RunAsync("dotnet", ["restore", Project, "--locked-mode"]);
            await BuildProcess.RunAsync("dotnet", ["build", Project, "--configuration", "Release", "--no-restore"]);
            await BuildProcess.RunAsync("docker", ["pull", envoyImage]);
            await BuildProcess.RunAsync("docker", ["pull", sdkImage]);
            try
            {
                await BuildProcess.RunAsync("docker", ["create", "--name", sourceContainer, envoyImage, "--version"]);
                await BuildProcess.RunAsync("docker", ["cp", sourceContainer + ":/usr/local/bin/envoy", Path.Combine(scratch, "envoy")]);
            }
            finally
            {
                await BuildProcess.RunAsync("docker", ["rm", "--force", sourceContainer]);
            }
            string binaryHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(scratch, "envoy"))));
            await WriteEnvironmentAsync(output, pins, platform, binaryHash);
            string user = (await BuildProcess.RunAsync("id", ["-u"])).Trim();
            string group = (await BuildProcess.RunAsync("id", ["-g"])).Trim();
            string container = "envoy-interop-" + Guid.NewGuid().ToString("N");
            try
            {
                await BuildProcess.RunAsync("docker", ["run", "--name", container, "--network", "none",
                    "--user", user + ":" + group, "--env", "HOME=/tmp",
                    "--mount", "type=bind,src=" + Path.GetFullPath("tests/Envoy.ControlPlane.EnvoyProbe/bin/Release/net10.0") + ",dst=/probe,readonly",
                    "--mount", "type=bind,src=" + scratch + ",dst=/envoy,readonly",
                    "--mount", "type=bind,src=" + Path.GetFullPath("tests/resources/envoy") + ",dst=/fixtures,readonly",
                    "--mount", "type=bind,src=" + output + ",dst=/results",
                    "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1", sdkImage,
                    "dotnet", "/probe/Envoy.ControlPlane.EnvoyProbe.dll", "/envoy/envoy", "/fixtures/bootstrap-ads.yaml", "/results"]);
            }
            finally
            {
                // Remove only this run's container, including after a bounded CLI failure.
                await BuildProcess.RunAsync("docker", ["rm", "--force", container]);
            }
            string version = File.ReadAllText(Path.Combine(output, "envoy-version.txt"));
            Require(version.Contains(pins["sourceCommit"]!.GetValue<string>() + "/" + pins["version"]!.GetValue<string>() + "/", StringComparison.Ordinal),
                "Envoy binary version/source differs from its recorded release");
            foreach (bool ordered in new[] { false, true })
            {
                string mode = ordered ? "ordered" : "unordered";
                var report = JsonNode.Parse(File.ReadAllText(Path.Combine(output, mode, "report.json")))!.AsObject();
                EnvoyResults.Verify(report, ordered);
                Require(report["architecture"]!.GetValue<string>() == (platform == "linux/amd64" ? "X64" : "Arm64"),
                    "Envoy probe architecture differs from the selected image platform");
            }
            foreach (string scenario in new[] { "delta", "ttl" })
            {
                var report = JsonNode.Parse(File.ReadAllText(Path.Combine(output, scenario, "report.json")))!.AsObject();
                if (scenario == "delta") EnvoyResults.VerifyDelta(report);
                else EnvoyResults.VerifyTtl(report);
                Require(report["architecture"]!.GetValue<string>() == (platform == "linux/amd64" ? "X64" : "Arm64"),
                    "Envoy root probe architecture differs from the selected image platform");
            }
            Console.WriteLine("Released Envoy: 35 V31-V33 checks passed for SotW/ADS, Delta/ADS and TTL on " + platform);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task WriteEnvironmentAsync(string output, JsonNode pins, string platform, string binaryHash)
    {
        var baseline = JsonNode.Parse(File.ReadAllText("upstream-baseline.json"))!;
        var environment = new
        {
            revision = (await BuildProcess.RunAsync("git", ["rev-parse", "HEAD"])).Trim(),
            workingTreeDirty = (await BuildProcess.RunAsync("git", ["status", "--porcelain"])).Length > 0,
            behaviorCommit = baseline["goControlPlane"]!["commit"]!.GetValue<string>(),
            schemaCommit = baseline["envoyApi"]!["commit"]!.GetValue<string>(),
            bindingLockSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes("schemas/bindings.lock.json"))),
            platform,
            binarySha256 = binaryHash,
            images = pins,
        };
        await File.WriteAllTextAsync(Path.Combine(output, "environment.json"),
            JsonSerializer.Serialize(environment, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
