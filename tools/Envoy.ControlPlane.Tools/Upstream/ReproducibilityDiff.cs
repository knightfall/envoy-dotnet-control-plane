using System.Text.Json;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ReproducibilityDiff
{
    internal static JsonObject Create(JsonObject before, JsonObject after)
    {
        VerifySnapshot(before);
        VerifySnapshot(after);
        var changes = new JsonObject
        {
            ["inputs"] = Compare(before["inputs"]!.AsObject(), after["inputs"]!.AsObject()),
        };
        foreach (string section in new[] { "files", "derivedFiles", "pgvRules", "derivedRules" })
        {
            changes.Add(section, Compare(before["descriptors"]![section]!.AsObject(), after["descriptors"]![section]!.AsObject()));
        }
        bool changed = changes.Any(section => section.Value!.AsArray().Count > 0);
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["beforeRevision"] = before["revision"]!.DeepClone(),
            ["afterRevision"] = after["revision"]!.DeepClone(),
            ["hasChanges"] = changed,
            ["changes"] = changes,
            ["validationAcceptance"] = false,
            ["coverage"] = after["coverage"]!.DeepClone(),
            ["verificationSelection"] = new JsonObject
            {
                ["policy"] = "Conservative full replay; no dependency-level or case-level pruning",
                ["goDotnetCorpora"] = JsonSerializer.SerializeToNode(changed ? RootDifferentialVerification.ModeNames() : []),
                ["nativeTests"] = changed ? "Complete solution and all fresh package consumers" : "None selected",
                ["commands"] = JsonSerializer.SerializeToNode(changed ? new[]
                {
                    "bindings generate --cache <source-cache>", "repository verify", "envoy verify",
                } : []),
                ["executionEvidence"] = "Selection only; no command execution or passing result is implied",
                ["performanceBaseline"] = "Manual only; not selected",
            },
            ["requiredVerification"] = changed
                ? "Regenerate and run repository verify, including every declared Go/.NET validation case. Changed/new constraints remain unproven until T26/GV establishes their coverage; this report cannot approve validator claims."
                : "No recorded input or descriptor/rule change. Existing evidence remains scoped to its original commit, cases and platform; T26/GV is still incomplete.",
        };
    }

    private static JsonArray Compare(JsonObject before, JsonObject after)
    {
        var rows = new JsonArray();
        foreach (string key in before.Select(item => item.Key).Union(after.Select(item => item.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool existed = before.TryGetPropertyValue(key, out var oldValue);
            bool exists = after.TryGetPropertyValue(key, out var newValue);
            if (existed && exists && JsonNode.DeepEquals(oldValue, newValue))
            {
                continue;
            }
            string change = "changed";
            if (!existed)
            {
                change = "added";
            }
            else if (!exists)
            {
                change = "removed";
            }
            rows.Add(new JsonObject
            {
                ["key"] = key,
                ["change"] = change,
                ["before"] = oldValue?.DeepClone(),
                ["after"] = newValue?.DeepClone(),
            });
        }
        return rows;
    }

    private static void VerifySnapshot(JsonObject snapshot)
    {
        Require(snapshot["schemaVersion"]?.GetValue<int>() == 1 && snapshot["kind"]?.GetValue<string>() == "envoy-control-plane-reproducibility",
            "Unsupported reproducibility snapshot");
        Require(snapshot["revision"] is JsonValue && snapshot["inputs"] is JsonObject && snapshot["coverage"] is JsonObject,
            "Incomplete reproducibility snapshot");
        foreach (string section in new[] { "files", "derivedFiles", "pgvRules", "derivedRules" })
        {
            Require(snapshot["descriptors"]?[section] is JsonObject, "Missing descriptor snapshot section: " + section);
        }
    }
}
