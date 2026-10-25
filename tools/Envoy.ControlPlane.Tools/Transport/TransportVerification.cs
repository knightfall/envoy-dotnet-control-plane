using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class TransportVerification
{
    private const string HostProject = "tests/Envoy.ControlPlane.TransportProbe/Envoy.ControlPlane.TransportProbe.csproj";
    private const string Oracle = "tests/resources/GoOracle";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args is ["verify"] or ["benchmark"], "Usage: transport <verify|benchmark> (run bindings generate first)");
            Require(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Transport probe supports Linux and macOS");
            Environment.CurrentDirectory = RepositoryPaths.FindRepositoryRoot();
            await RunProbeAsync(args[0] == "benchmark");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static async Task RunProbeAsync(bool benchmark)
    {
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/transport");
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }
        Directory.CreateDirectory(output);
        string scratch = Path.Combine(Path.GetTempPath(), "envoy-transport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await RunPreparedProbeAsync(benchmark, output, scratch);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task RunPreparedProbeAsync(bool benchmark, string output, string scratch)
    {
        int processors = Math.Min(4, Environment.ProcessorCount);
        var environment = new Dictionary<string, string>
        {
            ["GOWORK"] = "off",
            ["GOMAXPROCS"] = processors.ToString(),
            ["DOTNET_PROCESSOR_COUNT"] = processors.ToString(),
        };
        await ResourceVerification.VerifyGoModulesAsync();
        string grpcModule = await BuildProcess.RunAsync("go", ["list", "-mod=readonly", "-m", "-json", "google.golang.org/grpc"], Oracle, environment);
        await File.WriteAllTextAsync(Path.Combine(output, "grpc-module.json"), grpcModule);
        await BuildProcess.RunAsync("dotnet", ["restore", HostProject, "--locked-mode"]);
        await BuildProcess.RunAsync("dotnet", ["build", HostProject, "--configuration", "Release", "--no-restore"]);
        string probe = Path.Combine(scratch, "transport-probe");
        await BuildProcess.RunAsync("go", ["build", "-mod=readonly", "-o", probe, "./transport"], Oracle, environment);
        string certificates = Path.Combine(scratch, "certificates");
        await BuildProcess.RunAsync(probe, ["certificates", certificates], environment: environment);
        string revision = (await BuildProcess.RunAsync("git", ["rev-parse", "HEAD"])).Trim();
        string changes = await BuildProcess.RunAsync("git", ["status", "--porcelain"]);
        var details = new JsonObject
        {
            ["revision"] = revision,
            ["workingTreeDirty"] = changes.Length > 0,
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["hostProcessors"] = Environment.ProcessorCount,
            ["runtimeProcessorHint"] = processors,
            ["runnerImage"] = Environment.GetEnvironmentVariable("ImageOS"),
            ["runnerImageVersion"] = Environment.GetEnvironmentVariable("ImageVersion"),
            ["go"] = (await BuildProcess.RunAsync("go", ["version"])).Trim(),
        };
        await WriteJsonAsync(output, "environment.json", details);
        var checks = new JsonArray();
        var measurements = new JsonArray();
        string[] modes = OperatingSystem.IsLinux() ? ["plaintext", "mtls-tls12", "mtls-tls13"] : ["plaintext", "mtls-tls12"];
        foreach (string mode in modes)
        {
            await RunModeAsync(mode, mode == "plaintext" ? "" : certificates, benchmark, probe, output, environment, checks, measurements);
        }
        Require(checks.Count == 14 + ((modes.Length - 1) * 16), "Transport probe omitted functional checks");
        Require(measurements.Count == (benchmark ? modes.Length * 30 : 0), "Transport probe omitted measured trials");
        await File.WriteAllTextAsync(Path.Combine(output, "summary.md"), TransportSummary.Format(details, checks, measurements));
        Console.WriteLine($"Transport probe passed {checks.Count} functional checks; recorded {measurements.Count} trials. Performance has no pass/fail threshold.");
    }

    private static async Task RunModeAsync(string mode, string certificates, bool benchmark, string probe, string output,
        IReadOnlyDictionary<string, string> environment, JsonArray checks, JsonArray measurements)
    {
        environment = new Dictionary<string, string>(environment)
        {
            ["ENVOY_PROBE_TLS_PROTOCOL"] = mode.EndsWith("tls13", StringComparison.Ordinal) ? "tls13" : "tls12",
        };
        await using var go = await TransportServer.StartAsync(probe, ["serve"], certificates, Path.Combine(output, "go-" + mode + ".log"), environment);
        string host = Path.GetFullPath("tests/Envoy.ControlPlane.TransportProbe/bin/Release/net10.0/Envoy.ControlPlane.TransportProbe.dll");
        await using var dotnet = await TransportServer.StartAsync("dotnet", [host], certificates, Path.Combine(output, "dotnet-" + mode + ".log"), environment);
        foreach (var (name, server) in new[] { ("go", go), ("dotnet", dotnet) })
        {
            string response = await BuildProcess.RunAsync(probe, ["verify", server.RpcAddress, server.AdminAddress, certificates], environment: environment);
            foreach (var row in ParseChecks(response, mode, name))
            {
                checks.Add(row);
            }
            await WriteJsonAsync(output, "checks.json", checks);
            await WriteJsonAsync(output, name + "-" + mode + "-runtime.json", await server.ReadStatsAsync());
        }
        if (!benchmark)
        {
            return;
        }
        foreach (var (size, streams, connections) in new[] { (1024, 1, 1), (65536, 1, 1), (65536, 16, 1), (65536, 16, 16), (1048576, 1, 1) })
        {
            for (int trial = 1; trial <= 3; trial++)
            {
                var order = trial % 2 == 1 ? new[] { ("go", go), ("dotnet", dotnet) } : [("dotnet", dotnet), ("go", go)];
                foreach (var (name, server) in order)
                {
                    string response = await BuildProcess.RunAsync(probe,
                        ["measure", server.RpcAddress, server.AdminAddress, certificates, size.ToString(), streams.ToString(), connections == 1 ? "shared" : "separate"],
                        environment: environment);
                    var row = JsonNode.Parse(response)!.AsObject();
                    Require(row["operations"]!.GetValue<int>() > 0 && row["latencySamples"]!.GetValue<int>() > 0, "Empty transport measurement");
                    row["server"] = name;
                    row["mode"] = mode;
                    row["trial"] = trial;
                    measurements.Add(row);
                    await WriteJsonAsync(output, "measurements.json", measurements);
                }
            }
        }
    }

    private static List<JsonObject> ParseChecks(string response, string mode, string server)
    {
        var rows = response.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
        string[] expected = ["DuplexHalfCloseUnaryAndMetadata", "ExplicitStatus", "RequestSizeBoundaryAndLargeResponse", "StreamingBodyBeyondHttpLimit",
            "UnknownMethod", "CancellationAndDeadlineCleanup", "SlowReaderOtherStreamProgress"];
        var names = expected.ToHashSet(StringComparer.Ordinal);
        if (mode.StartsWith("mtls", StringComparison.Ordinal))
        {
            names.Add("MutualTlsAdmission");
        }
        Require(rows.Count == names.Count && names.SetEquals(rows.Select(row => row["test"]!.GetValue<string>())), "Transport check membership differs");
        foreach (var row in rows)
        {
            Require(row["passed"]!.GetValue<bool>(), "Transport functional check failed");
            row["server"] = server;
            row["mode"] = mode;
        }
        return rows;
    }

    private static Task WriteJsonAsync(string directory, string name, JsonNode value) =>
        File.WriteAllTextAsync(Path.Combine(directory, name), value.ToJsonString(JsonOptions) + Environment.NewLine);
}
