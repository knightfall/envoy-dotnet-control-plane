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

    [Theory]
    [InlineData("delta")]
    [InlineData("delta-ttl")]
    public void CompleteRootReport_AcceptsItsDeclaredProtocol(string protocol)
    {
        var report = RootReport(protocol);
        VerifyRoot(report, protocol);
        Assert.Equal(protocol == "delta" ? 9 : 6, report["checks"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("delta", "missing", "required V32 check")]
    [InlineData("delta", "failed", "failed V32 check")]
    [InlineData("delta", "protocol", "Delta protocol differs")]
    [InlineData("delta-ttl", "missing", "required V33 check")]
    [InlineData("delta-ttl", "failed", "failed V33 check")]
    [InlineData("delta-ttl", "protocol", "TTL protocol differs")]
    public void IncompleteRootReport_IsRejected(string protocol, string mutation, string diagnostic)
    {
        var report = RootReport(protocol);
        if (mutation == "missing") report["checks"]!.AsArray().RemoveAt(0);
        if (mutation == "failed") report["checks"]![1]!["passed"] = false;
        if (mutation == "protocol") report["protocol"] = "wrong";

        var failure = Assert.Throws<InvalidOperationException>(() => VerifyRoot(report, protocol));
        Assert.Contains(diagnostic, failure.Message, StringComparison.Ordinal);
    }

    private static void VerifyRoot(JsonObject report, string protocol)
    {
        if (protocol == "delta") EnvoyResults.VerifyDelta(report);
        else EnvoyResults.VerifyTtl(report);
    }

    private static JsonObject RootReport(string protocol)
    {
        string[] names = protocol == "delta"
            ? ["delta-initial-traffic-and-types", "delta-endpoint-update", "delta-route-update", "delta-removal",
                "delta-readd", "delta-disconnect-retains-traffic", "delta-reconnect-initial-versions",
                "delta-post-reconnect-update", "delta-cleanup"]
            : ["ttl-initial-traffic", "ttl-refresh-without-config-resend", "ttl-expiry-and-counter",
                "ttl-cache-retained", "ttl-explicit-repair", "ttl-cleanup"];
        var rows = new JsonArray();
        foreach (string name in names) rows.Add(new JsonObject { ["name"] = name, ["passed"] = true });
        return new JsonObject { ["protocol"] = protocol, ["checks"] = rows };
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
