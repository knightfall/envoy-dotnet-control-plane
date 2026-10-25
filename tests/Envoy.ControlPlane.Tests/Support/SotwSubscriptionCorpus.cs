using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Streams;

namespace Envoy.ControlPlane.Tests.Support;

internal static class SotwSubscriptionCorpus
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static JsonArray ReadFixtures()
    {
        using var stream = typeof(SotwSubscriptionCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.SotwCases.json")!;
        return JsonNode.Parse(stream)!.AsArray();
    }

    internal static SotwCaseResult Evaluate(JsonNode fixture)
    {
        string typeUrl = fixture["typeUrl"]!.GetValue<string>();
        bool disableAll = fixture["disableAll"]!.GetValue<bool>();
        string[] disabledTypes = Strings(fixture["disabledTypes"]);
        var options = new SotwOptions
        {
            DisableLegacyWildcard = disableAll,
            LegacyWildcardDisabledTypes = new HashSet<string>(disabledTypes, StringComparer.Ordinal),
        };
        string[] initial = Strings(fixture["initial"]);
        var subscription = new SotwSubscription(initial, options.IsLegacyWildcardActive(typeUrl));
        var observed = new List<SotwObservation> { Observe(subscription) };
        var steps = new List<SotwStep>();
        foreach (var step in fixture["steps"]!.AsArray())
        {
            string kind = step!["kind"]!.GetValue<string>();
            string[]? names = step["names"] is null ? null : Strings(step["names"]);
            var versions = step["returned"]?.AsObject().ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);
            steps.Add(new SotwStep(kind, names, versions));
            switch (kind)
            {
                case "subscribe":
                    subscription.SetResourceSubscription(names);
                    break;
                case "returned":
                    subscription.SetReturnedResources(versions!);
                    break;
                default:
                    throw new InvalidOperationException("Unknown SotW fixture step: " + kind);
            }
            observed.Add(Observe(subscription));
        }
        return new SotwCaseResult(fixture["id"]!.GetValue<string>(), typeUrl, disableAll, disabledTypes, initial, steps.ToArray(), observed.ToArray());
    }

    internal static void WriteResults(SotwCaseResult[] results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sotw-subscription-corpus.json"),
        JsonSerializer.Serialize(results, JsonOptions) + "\n");

    private static string[] Strings(JsonNode? values) => values?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];

    private static SotwObservation Observe(SotwSubscription subscription) => new(subscription.IsWildcard,
        subscription.SubscribedResources.Order(StringComparer.Ordinal).ToArray(), subscription.SubscribedPrefixes.Order(StringComparer.Ordinal).ToArray(),
        subscription.ReturnedResources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
}

internal sealed record SotwStep(string Kind, string[]? Names, Dictionary<string, string>? Returned);
internal sealed record SotwObservation(bool Wildcard, string[] Exact, string[] Prefixes, Dictionary<string, string> Returned);
internal sealed record SotwCaseResult(string Id, string TypeUrl, bool DisableAll, string[] DisabledTypes, string[] Initial, SotwStep[] Steps, SotwObservation[] Observed);
