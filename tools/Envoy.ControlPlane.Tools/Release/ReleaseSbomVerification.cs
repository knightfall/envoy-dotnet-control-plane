using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ReleaseSbomVerification
{
    internal static void VerifyDependencies(JsonNode sbom, IEnumerable<JsonNode> packageLocks)
    {
        var components = sbom["packages"]!.AsArray().Select(package =>
            package!["name"]!.GetValue<string>() + "@" + package["versionInfo"]?.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in packageLocks)
        {
            foreach (var framework in manifest["dependencies"]!.AsObject())
            {
                foreach (var (id, package) in framework.Value!.AsObject())
                {
                    if (package!["type"]!.GetValue<string>() is not ("Direct" or "Transitive")) continue;
                    string identity = id + "@" + package["resolved"]!.GetValue<string>();
                    Require(components.Contains(identity), "SBOM omitted a locked runtime dependency: " + identity);
                }
            }
        }
    }
}
