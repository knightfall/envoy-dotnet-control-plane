using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Tools;

internal static class DiscoveryTraceComparer
{
    internal static void Compare(string id, JsonObject candidate, JsonObject reference)
        => CompareCore(id, candidate, reference, requestNonceField: 4, protocol: "SotW");

    internal static void CompareDelta(string id, JsonObject candidate, JsonObject reference)
        => CompareCore(id, candidate, reference, requestNonceField: 3, protocol: "Delta");

    private static void CompareCore(string id, JsonObject candidate, JsonObject reference, int requestNonceField, string protocol)
    {
        var actualEvents = candidate["events"]!.AsArray();
        var expectedEvents = reference["events"]!.AsArray();
        if (actualEvents.Count != expectedEvents.Count)
        {
            throw Difference(id, "event count", expectedEvents.Count.ToString(), actualEvents.Count.ToString(), protocol);
        }

        var nonces = new NonceMapping();
        for (int index = 0; index < expectedEvents.Count; index++)
        {
            string[] actual = ParseEvent(actualEvents[index]!.GetValue<string>(), protocol);
            string[] expected = ParseEvent(expectedEvents[index]!.GetValue<string>(), protocol);
            if (actual[0] != expected[0])
            {
                throw Difference(id, $"event {index} kind", expected[0], actual[0], protocol);
            }

            int nonceField = expected[0] switch
            {
                "response" or "send" => 3,
                "request" => requestNonceField,
                _ => -1,
            };
            for (int field = 1; field < expected.Length; field++)
            {
                bool matches = field == nonceField
                    ? nonces.Match(actual[field], expected[field], expected[0] == "response")
                    : actual[field] == expected[field];
                if (!matches)
                {
                    throw Difference(id, $"event {index} {expected[0]} field {field}", expected[field], actual[field], protocol);
                }
            }
        }

        var actualMetadata = candidate.DeepClone().AsObject();
        var expectedMetadata = reference.DeepClone().AsObject();
        actualMetadata.Remove("events");
        expectedMetadata.Remove("events");
        if (!JsonNode.DeepEquals(actualMetadata, expectedMetadata))
        {
            throw Difference(id, "error/watch lifecycle", expectedMetadata.ToJsonString(), actualMetadata.ToJsonString(), protocol);
        }
    }

    private static string[] ParseEvent(string value, string protocol)
    {
        string[] fields = value.Split('|');
        int count = fields[0] switch
        {
            "open" or "close" => 2,
            "request" => 7,
            "watch" => 6,
            "response" or "send" => 4,
            _ => throw new InvalidDataException($"Unknown {protocol} trace event: " + value),
        };
        if (fields.Length != count)
        {
            throw new InvalidDataException($"Malformed {protocol} trace event: " + value);
        }
        return fields;
    }

    private static InvalidDataException Difference(string id, string location, string expected, string actual, string protocol) =>
        new($"{id}: {protocol} trace differs at {location}: Go={expected}; .NET={actual}");

    private sealed class NonceMapping
    {
        private readonly Dictionary<string, string> candidateToReference = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> referenceToCandidate = new(StringComparer.Ordinal);

        internal bool Match(string candidate, string reference, bool generated)
        {
            if (candidate.Length == 0 || reference.Length == 0)
            {
                return candidate == reference;
            }
            if (candidateToReference.TryGetValue(candidate, out string? mapped))
            {
                return mapped == reference;
            }
            if (referenceToCandidate.ContainsKey(reference) || (!generated && candidate != reference))
            {
                return false;
            }

            // Literal stale/override tokens reserve their identity too. A later generated
            // nonce cannot silently collapse onto one of those tokens.
            candidateToReference.Add(candidate, reference);
            referenceToCandidate.Add(reference, candidate);
            return true;
        }
    }
}
