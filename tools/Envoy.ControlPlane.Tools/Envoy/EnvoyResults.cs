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

    internal static void Verify(JsonObject report, bool ordered)
    {
        Require(report["ordered"]!.GetValue<bool>() == ordered, "Envoy report ordering mode differs");
        var rows = report["checks"]!.AsArray();
        Require(rows.Select(row => row!["name"]!.GetValue<string>()).SequenceEqual(Checks),
            "Envoy report omitted, duplicated or reordered a required V31 check");
        Require(rows.All(row => row!["passed"]!.GetValue<bool>()), "Envoy report contains a failed V31 check");
    }
}
