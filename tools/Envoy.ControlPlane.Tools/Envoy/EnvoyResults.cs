using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class EnvoyResults
{
    private static readonly string[] Checks =
    [
        "bootstrap-and-initial-traffic", "endpoint-update", "route-update", "nack-association",
        "nack-retains-accepted-config", "explicit-repair", "disconnect-retains-traffic",
        "reconnect-resubscribes", "post-reconnect-update", "cleanup",
    ];

    private static readonly string[] DeltaChecks =
    [
        "delta-initial-traffic-and-types", "delta-endpoint-update", "delta-route-update", "delta-removal",
        "delta-readd", "delta-disconnect-retains-traffic", "delta-reconnect-initial-versions",
        "delta-post-reconnect-update", "delta-cleanup",
    ];

    private static readonly string[] TtlChecks =
    [
        "ttl-initial-traffic", "ttl-refresh-without-config-resend", "ttl-expiry-and-counter",
        "ttl-cache-retained", "ttl-explicit-repair", "ttl-cleanup",
    ];

    internal static void Verify(JsonObject report, bool ordered)
    {
        Require(report["ordered"]!.GetValue<bool>() == ordered, "Envoy report ordering mode differs");
        VerifyChecks(report, Checks, "V31");
    }

    internal static void VerifyDelta(JsonObject report)
    {
        Require(report["protocol"]!.GetValue<string>() == "delta", "Envoy report Delta protocol differs");
        VerifyChecks(report, DeltaChecks, "V32");
    }

    internal static void VerifyTtl(JsonObject report)
    {
        Require(report["protocol"]!.GetValue<string>() == "delta-ttl", "Envoy report TTL protocol differs");
        VerifyChecks(report, TtlChecks, "V33");
    }

    private static void VerifyChecks(JsonObject report, string[] checks, string verification)
    {
        var rows = report["checks"]!.AsArray();
        Require(rows.Select(row => row!["name"]!.GetValue<string>()).SequenceEqual(checks),
            "Envoy report omitted, duplicated or reordered a required " + verification + " check");
        Require(rows.All(row => row!["passed"]!.GetValue<bool>()), "Envoy report contains a failed " + verification + " check");
    }
}
