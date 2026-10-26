using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Streams;

namespace Envoy.ControlPlane.Tests.Support;

internal static class DeltaSubscriptionCorpus
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static JsonArray ReadFixtures()
    {
        using var stream = typeof(DeltaSubscriptionCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.DeltaCases.json")!;
        return JsonNode.Parse(stream)!.AsArray();
    }

    internal static DeltaCaseResult Evaluate(JsonNode fixture)
    {
        bool legacy = fixture["allowLegacyWildcard"]!.GetValue<bool>();
        string[] subscribed = Strings(fixture["initialSubscribed"]);
        string[] unsubscribed = Strings(fixture["initialUnsubscribed"]);
        var initial = Versions(fixture["initialVersions"]);
        var subscription = new DeltaSubscription(subscribed, unsubscribed, initial, legacy);
        var observed = new List<DeltaObservation> { Observe(subscription) };
        var steps = new List<DeltaStep>();
        foreach (var step in fixture["steps"]!.AsArray())
        {
            string kind = step!["kind"]!.GetValue<string>();
            var additions = Strings(step["subscribed"]);
            var removals = Strings(step["unsubscribed"]);
            var returned = Versions(step["returned"]);
            steps.Add(new DeltaStep(kind, additions, removals, returned));
            switch (kind)
            {
                case "update":
                    subscription.UpdateResourceSubscriptions(additions, removals);
                    break;
                case "returned":
                    subscription.SetReturnedResources(returned);
                    break;
                case "reconnect":
                    subscription = new DeltaSubscription(additions, removals, returned, legacy);
                    break;
                default:
                    throw new InvalidOperationException("Unknown Delta subscription step: " + kind);
            }
            observed.Add(Observe(subscription));
        }
        return new DeltaCaseResult(fixture["id"]!.GetValue<string>(), legacy, subscribed, unsubscribed, initial, steps.ToArray(), observed.ToArray());
    }

    internal static void WriteResults(DeltaCaseResult[] results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "delta-subscription-corpus.json"),
        JsonSerializer.Serialize(results, JsonOptions) + "\n");

    private static string[] Strings(JsonNode? values) => values?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];

    private static Dictionary<string, string> Versions(JsonNode? values) => values?.AsObject()
        .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);

    private static DeltaObservation Observe(DeltaSubscription subscription) => new(subscription.IsWildcard,
        subscription.SubscribedResources.Order(StringComparer.Ordinal).ToArray(), subscription.SubscribedPrefixes.Order(StringComparer.Ordinal).ToArray(),
        subscription.ReturnedResources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
}

internal sealed record DeltaStep(string Kind, string[] Subscribed, string[] Unsubscribed, Dictionary<string, string> Returned);
internal sealed record DeltaObservation(bool Wildcard, string[] Exact, string[] Prefixes, Dictionary<string, string> Returned);
internal sealed record DeltaCaseResult(string Id, bool AllowLegacyWildcard, string[] InitialSubscribed, string[] InitialUnsubscribed,
    Dictionary<string, string> InitialVersions, DeltaStep[] Steps, DeltaObservation[] Observed);
