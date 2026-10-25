using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.Tests.Support;

internal static class SnapshotCorpus
{
    private static readonly MessageDescriptor[] Descriptors = [Cluster.Descriptor, ClusterLoadAssignment.Descriptor,
        Listener.Descriptor, RouteConfiguration.Descriptor, ScopedRouteConfiguration.Descriptor, FilterChain.Descriptor];
    private static readonly JsonParser Parser = new(JsonParser.Settings.Default.WithTypeRegistry(
        TypeRegistry.FromMessages([.. Descriptors, HttpConnectionManager.Descriptor])));

    internal static JsonArray ReadFixtures()
    {
        using var stream = typeof(SnapshotCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.SnapshotCases.json")!;
        return JsonNode.Parse(stream)!.AsArray();
    }

    internal static SnapshotCaseResult Evaluate(JsonNode fixture)
    {
        var groups = new Dictionary<string, IEnumerable<ResourceEntry>>(StringComparer.Ordinal);
        var wireGroups = new List<WireGroup>();
        foreach (var group in fixture["groups"]!.AsArray())
        {
            string typeUrl = group!["typeUrl"]!.GetValue<string>();
            var descriptor = Descriptors.Single(item => ResourceTypeUrls.Prefix + item.FullName == typeUrl);
            var entries = new List<ResourceEntry>();
            var wire = new List<WireEntry>();
            foreach (var entry in group["entries"]!.AsArray())
            {
                var message = Parser.Parse(entry!["message"]!.ToJsonString(), descriptor);
                long? ttl = entry["ttlNanoseconds"]?.GetValue<long>();
                entries.Add(new ResourceEntry(message, ttl is { } nanos ? TimeSpan.FromTicks(nanos / 100) : null));
                wire.Add(new WireEntry(Convert.ToBase64String(message.ToByteArray()), ttl));
            }
            groups.Add(typeUrl, entries);
            wireGroups.Add(new WireGroup(typeUrl, wire.ToArray()));
        }
        string version = fixture["version"]!.GetValue<string>();
        var snapshot = new Snapshot(version, groups);
        bool consistent = true;
        try
        {
            snapshot.EnsureConsistent();
        }
        catch (InvalidOperationException)
        {
            consistent = false;
        }
        var names = groups.Keys.ToDictionary(type => type,
            type => snapshot.GetResources(type)!.Keys.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var versions = groups.Keys.ToDictionary(type => type, snapshot.GetVersion, StringComparer.Ordinal);
        var ttls = groups.Keys.ToDictionary(type => type,
            type => snapshot.GetResourcesAndTtl(type)!.ToDictionary(item => item.Key,
                item => item.Value.Ttl is { } ttl ? (long?)(ttl.Ticks * 100) : null, StringComparer.Ordinal), StringComparer.Ordinal);
        return new SnapshotCaseResult(fixture["id"]!.GetValue<string>(), version, wireGroups.ToArray(), consistent, names, versions, ttls);
    }

    internal static void WriteResults(SnapshotCaseResult[] results) => File.WriteAllText(
        Path.Combine(AppContext.BaseDirectory, "snapshot-corpus.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}

internal sealed record WireEntry(string Data, long? TtlNanoseconds);
internal sealed record WireGroup(string TypeUrl, WireEntry[] Entries);
internal sealed record SnapshotCaseResult(string Id, string Version, WireGroup[] Groups, bool Consistent,
    Dictionary<string, string[]> ResourceNames, Dictionary<string, string> Versions, Dictionary<string, Dictionary<string, long?>> Ttls);
