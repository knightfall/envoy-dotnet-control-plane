using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using Pgv = Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests.Support;

internal static class FieldRuleCorpus
{
    private static readonly Lazy<FieldRuleResult[]> Cached = new(Evaluate);
    internal static FieldRuleResult[] Results => Cached.Value;

    internal static JsonArray ReadCases()
    {
        using var source = typeof(FieldRuleCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Validation.Tests.FieldCases.json")
            ?? throw new InvalidOperationException("Missing field-rule corpus");
        return JsonNode.Parse(source)!.AsArray();
    }

    private static FieldRuleResult[] Evaluate()
    {
        var registry = TypeRegistry.FromFiles(ValidationCorpus.Files);
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(registry));
        var validator = new Validator();
        var results = new List<FieldRuleResult>();
        foreach (var group in ReadCases())
        {
            string name = group!["field"]!.GetValue<string>();
            int separator = name.LastIndexOf('.');
            var descriptor = registry.Find(name[..separator]) ?? throw new InvalidOperationException("Unknown field owner: " + name);
            var field = descriptor.FindFieldByName(name[(separator + 1)..]) ?? throw new InvalidOperationException("Unknown field: " + name);
            string[] paths = PgvRuleInventory.EnumerateRulePaths(field.GetOptions().GetExtension(Pgv.Rules), "").Order(StringComparer.Ordinal).ToArray();
            foreach (var item in group["cases"]!.AsArray())
            {
                string id = name + "/" + item!["id"]!.GetValue<string>();
                bool? actual = null;
                string? error = null;
                string data = "";
                string[] violations = [];
                try
                {
                    var input = new JsonObject { [field.JsonName] = Expand(item) };
                    var message = parser.Parse(input.ToJsonString(), descriptor);
                    data = Convert.ToBase64String(message.ToByteArray());
                    var result = validator.Validate(message, failFast: false);
                    violations = result.Violations.Where(violation => violation.Field?.Elements.FirstOrDefault()?.FieldName == field.Name)
                        .Select(violation => violation.RuleId + "|" + violation.Field.GetPath()).ToArray();
                    actual = violations.Length == 0;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    error = failure.GetType().Name + ": " + failure.Message;
                }
                results.Add(new(id, descriptor.FullName, field.Name, item["valid"]!.GetValue<bool>(), actual, data, paths, violations, error));
            }
        }
        return results.ToArray();
    }

    private static JsonNode? Expand(JsonNode item)
    {
        var value = item["value"];
        int repeat = item["repeat"]?.GetValue<int>() ?? 1;
        if (value is JsonArray array)
        {
            return new JsonArray(Enumerable.Range(0, repeat).SelectMany(_ => array.Select(element => element?.DeepClone())).ToArray());
        }
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text))
        {
            return JsonValue.Create(string.Concat(Enumerable.Repeat(text, repeat)) + (item["suffix"]?.GetValue<string>() ?? ""));
        }
        return value?.DeepClone();
    }
}

internal sealed record FieldRuleResult(string Id, string Type, string Field, bool ExpectedValid, bool? ActualValid,
    string Data, string[] RulePaths, string[] Violations, string? Error);
