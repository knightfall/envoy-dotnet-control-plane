using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Tools;
using Envoy.ControlPlane.Validation.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class CatalogCoverageTests
{
    [Fact]
    public void Catalog_AllRuleCombinations_HaveBoundaryEvidence()
    {
        byte[] original = Read("catalog.pb");
        byte[] derived = Read("validation.catalog.pb");
        var extensions = new ExtensionRegistry
        {
            Validate.ValidateExtensions.Rules, Validate.ValidateExtensions.Required,
            Validate.ValidateExtensions.Disabled, Validate.ValidateExtensions.Ignored,
        };
        var inventory = PgvRuleInventory.Create(FileDescriptorSet.Parser.WithExtensionRegistry(extensions).ParseFrom(original));
        var fields = inventory["fields"]!.AsArray().ToDictionary(item => item!["field"]!.GetValue<string>(), StringComparer.Ordinal);
        var evidence = FieldRuleCorpus.ReadCases();
        Assert.Equal(90, evidence.Count);
        foreach (var group in evidence)
        {
            string field = group!["field"]!.GetValue<string>();
            Assert.True(fields.ContainsKey(field), "Unknown evidence field: " + field);
            Assert.Equal(Paths(fields[field]!), Paths(group));
            Assert.Contains(group["cases"]!.AsArray(), item => item!["valid"]!.GetValue<bool>());
            bool noRejectionRule = fields[field]!["rules"]?["message"]?["skip"]?.GetValue<bool>() == true
                || fields[field]!["rules"]?["message"]?["required"]?.GetValue<bool>() == false;
            if (!noRejectionRule)
            {
                Assert.Contains(group["cases"]!.AsArray(), item => !item!["valid"]!.GetValue<bool>());
            }
        }
        string[] combinations = fields.Values.Select(item => Signature(item!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(86, combinations.Length);
        Assert.Equal(combinations, evidence.Select(item => Signature(item!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(59, evidence.SelectMany(item => Paths(item!)).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1244, fields.Count);
        Assert.Equal(127, inventory["oneofs"]!.AsArray().Count);

        var coverage = new JsonObject
        {
            ["originalDescriptorSha256"] = Convert.ToHexStringLower(SHA256.HashData(original)),
            ["derivedDescriptorSha256"] = Convert.ToHexStringLower(SHA256.HashData(derived)),
            ["fieldRuleCount"] = fields.Count,
            ["oneofOptionCount"] = inventory["oneofs"]!.AsArray().Count,
            ["rulePathCounts"] = inventory["rulePathCounts"]!.DeepClone(),
            ["combinations"] = new JsonArray(combinations.Select(signature => (JsonNode)new JsonObject
            {
                ["paths"] = JsonSerializer.SerializeToNode(signature.Split('+')),
                ["fields"] = JsonSerializer.SerializeToNode(fields.Where(item => Signature(item.Value!) == signature).Select(item => item.Key).Order(StringComparer.Ordinal)),
                ["evidenceFields"] = JsonSerializer.SerializeToNode(evidence.Where(item => Signature(item!) == signature).Select(item => item!["field"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "validation-coverage-catalog.json"),
            coverage.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string[] Paths(JsonNode group) => group["rulePaths"]!.AsArray().Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
    private static string Signature(JsonNode group) => string.Join('+', Paths(group));

    private static byte[] Read(string name)
    {
        using var stream = typeof(Address).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Protocol." + name)
            ?? throw new InvalidOperationException("Missing embedded catalog: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
