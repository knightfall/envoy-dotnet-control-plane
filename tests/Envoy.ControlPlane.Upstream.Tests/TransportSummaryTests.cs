using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class TransportSummaryTests
{
    [Fact]
    public void AllocationRegression_ExcludesEveryGroupForAffectedRuntimeAndRetainsOtherRuntime()
    {
        var environment = new JsonObject
        {
            ["revision"] = "probe-head",
            ["workingTreeDirty"] = false,
            ["os"] = "Linux",
            ["architecture"] = "X64",
            ["runtimeProcessorHint"] = 4,
            ["hostProcessors"] = 4,
        };
        var measurements = new JsonArray();
        AddTrials(measurements, "dotnet", ".NET 10.0.13", "plaintext", 1000, regress: false);
        AddTrials(measurements, "dotnet", ".NET 10.0.13", "mtls-tls13", 2000, regress: true);
        AddTrials(measurements, "go", "go1.26.4", "mtls-tls13", 512, regress: false);

        string report = TransportSummary.Format(environment, [], measurements);

        string[] dotnetRows = report.Split('\n').Where(line => line.Contains("| dotnet |", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, dotnetRows.Length);
        Assert.All(dotnetRows, row => Assert.EndsWith("| unavailable |", row));
        Assert.Contains("| 512 |", report.Split('\n').Single(line => line.Contains("| go |", StringComparison.Ordinal)));
        Assert.Contains("Allocation readings decreased for: .NET 10.0.13", report);
        Assert.Contains("not a zero-allocation result", report);
        Assert.Null(measurements[4]!["allocatedBytesPerResponse"]);
        Assert.Equal(3000, measurements[4]!["allocatedBytesBefore"]!.GetValue<int>());
        Assert.Equal(2000, measurements[4]!["allocatedBytesAfter"]!.GetValue<int>());
        Assert.Equal(1000.0, measurements[0]!["allocatedBytesPerResponse"]!.GetValue<double>());
    }

    private static void AddTrials(JsonArray measurements, string server, string runtime, string mode, int allocation, bool regress)
    {
        for (int trial = 0; trial < 3; trial++)
        {
            bool valid = !regress || trial != 1;
            measurements.Add(new JsonObject
            {
                ["server"] = server,
                ["runtime"] = runtime,
                ["mode"] = mode,
                ["size"] = 65536,
                ["streams"] = 16,
                ["connections"] = 1,
                ["operations"] = 100,
                ["responsesPerSecond"] = 50.0,
                ["p95us"] = 200.0,
                ["serverCpuSeconds"] = 0.01,
                ["allocationCounterValid"] = valid,
                ["allocatedBytesPerResponse"] = valid ? (double)allocation : null,
                ["allocatedBytesBefore"] = 3000,
                ["allocatedBytesAfter"] = valid ? 4000 : 2000,
            });
        }
    }
}
