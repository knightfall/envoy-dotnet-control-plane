using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Tools;

internal static class SourceSbom
{
    internal static JsonObject Create(JsonNode upstream, JsonNode validation, string version, string commit)
    {
        var packages = new JsonArray(Package("SPDXRef-Release", "Knightfall.Envoy.ControlPlane", version,
            "https://github.com/knightfall/envoy-dotnet-control-plane/tree/" + commit, null, "Apache-2.0"));
        var relationships = new JsonArray();
        foreach (var source in upstream["sources"]!.AsArray())
        {
            string id = "SPDXRef-Source-" + source!["id"]!.GetValue<string>();
            packages.Add(Package(id, source["id"]!.GetValue<string>(), source["commit"]!.GetValue<string>(),
                source["archive"]!["url"]!.GetValue<string>(), source["archive"]!["sha256"]!.GetValue<string>(),
                source["license"]!.GetValue<string>()));
            AddRelationship(id);
        }
        foreach (string name in new[] { "runtime", "migration" })
        {
            var source = validation[name]!;
            string id = "SPDXRef-Validation-" + name;
            packages.Add(Package(id, "protovalidate-" + name, source["commit"]!.GetValue<string>(),
                source["archiveUrl"]!.GetValue<string>(), source["archiveSha256"]!.GetValue<string>(), "NOASSERTION"));
            AddRelationship(id);
        }
        return new JsonObject
        {
            ["spdxVersion"] = "SPDX-2.2", ["dataLicense"] = "CC0-1.0", ["SPDXID"] = "SPDXRef-DOCUMENT",
            ["name"] = "Knightfall.Envoy.ControlPlane source inputs " + version,
            ["documentNamespace"] = "https://github.com/knightfall/envoy-dotnet-control-plane/sbom/" + commit + "/sources/" + Guid.NewGuid().ToString("N"),
            ["creationInfo"] = new JsonObject
            {
                ["creators"] = new JsonArray("Tool: Envoy.ControlPlane.Tools"),
                ["created"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            },
            ["documentDescribes"] = new JsonArray("SPDXRef-Release"),
            ["packages"] = packages, ["relationships"] = relationships,
            ["comment"] = "Pinned source/build inputs, including source-built evaluation and generated contracts. "
                + "Archive hashes identify original inputs; release evidence retains exact adaptations, licenses and generated fingerprints. "
                + "NuGet and other detected build dependencies are recorded by Microsoft SBOM Tool in the companion build SBOM.",
        };

        void AddRelationship(string id) => relationships.Add(new JsonObject
        {
            ["spdxElementId"] = "SPDXRef-Release", ["relationshipType"] = "GENERATED_FROM", ["relatedSpdxElement"] = id,
        });
    }

    private static JsonObject Package(string id, string name, string version, string location, string? hash, string license)
    {
        var package = new JsonObject
        {
            ["SPDXID"] = id, ["name"] = name, ["versionInfo"] = version, ["downloadLocation"] = location,
            ["filesAnalyzed"] = false, ["licenseConcluded"] = "NOASSERTION", ["licenseDeclared"] = license,
            ["copyrightText"] = "NOASSERTION",
        };
        if (hash is not null)
            package["checksums"] = new JsonArray(new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = hash });
        return package;
    }
}
