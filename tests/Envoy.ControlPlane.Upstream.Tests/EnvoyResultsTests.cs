using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class EnvoyResultsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteSuccessfulReport_AcceptsItsDeclaredMode(bool ordered)
    {
        var report = Report(ordered);
        EnvoyResults.Verify(report, ordered);
        Assert.Equal(10, report["checks"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("missing", "omitted, duplicated or reordered")]
    [InlineData("duplicate", "omitted, duplicated or reordered")]
    [InlineData("failed", "failed V31 check")]
    [InlineData("mode", "ordering mode differs")]
    public void IncompleteOrFailedReport_IsRejected(string mutation, string diagnostic)
    {
        var report = Report(false);
        if (mutation == "missing") report["checks"]!.AsArray().RemoveAt(9);
        if (mutation == "duplicate") report["checks"]![0]!["name"] = "cleanup";
        if (mutation == "failed") report["checks"]![3]!["passed"] = false;
        if (mutation == "mode") report["ordered"] = true;

        var failure = Assert.Throws<InvalidOperationException>(() => EnvoyResults.Verify(report, false));
        Assert.Contains(diagnostic, failure.Message, StringComparison.Ordinal);
    }

    private static JsonObject Report(bool ordered)
    {
        string[] names = ["bootstrap-and-initial-traffic", "endpoint-update", "route-update", "nack-association",
            "nack-retains-accepted-config", "explicit-repair", "disconnect-retains-traffic", "reconnect-resubscribes",
            "post-reconnect-update", "cleanup"];
        var rows = new JsonArray();
        foreach (string name in names) rows.Add(new JsonObject { ["name"] = name, ["passed"] = true });
        return new JsonObject { ["ordered"] = ordered, ["checks"] = rows };
    }
}
