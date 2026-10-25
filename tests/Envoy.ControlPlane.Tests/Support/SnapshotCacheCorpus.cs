using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests.Support;

internal static class SnapshotCacheCorpus
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static JsonArray ReadFixtures()
    {
        using var stream = typeof(SnapshotCacheCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.CacheCases.json")!;
        return JsonNode.Parse(stream)!.AsArray();
    }

    internal static async Task<CacheCaseResult> EvaluateAsync(JsonNode fixture)
    {
        string typeUrl = fixture["typeUrl"]!.GetValue<string>();
        bool ads = fixture["ads"]!.GetValue<bool>();
        var cache = new SnapshotCache(ads);
        var initial = ReadSnapshot(fixture["initial"], typeUrl);
        if (initial is not null)
        {
            await cache.SetSnapshotAsync("node", BuildSnapshot(initial, typeUrl));
        }
        string requestVersion = fixture["requestVersion"]!.GetValue<string>();
        string[] names = Strings(fixture["names"]!);
        string[] prefixes = Strings(fixture["prefixes"]!);
        bool wildcard = fixture["wildcard"]!.GetValue<bool>();
        var known = fixture["known"]!.AsObject().ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        using var watch = cache.CreateWatch(Request(typeUrl, requestVersion, "node", names), Subscription(names, wildcard, known, prefixes), responses.Writer);
        bool canceled = false;
        var observed = new List<CacheObservation> { Observe(cache, responses.Reader, canceled, typeUrl) };
        var events = new List<WireCacheEvent>();
        foreach (var step in fixture["events"]!.AsArray())
        {
            string kind = step!["kind"]!.GetValue<string>();
            var snapshot = ReadSnapshot(step["snapshot"], typeUrl);
            events.Add(new WireCacheEvent(kind, snapshot));
            switch (kind)
            {
                case "publish":
                    await cache.SetSnapshotAsync("node", BuildSnapshot(snapshot!, typeUrl));
                    break;
                case "cancel":
                    watch.Dispose();
                    canceled = true;
                    break;
                case "clear":
                    cache.ClearSnapshot("node");
                    break;
                default:
                    throw new InvalidOperationException("Unknown cache fixture event: " + kind);
            }
            observed.Add(Observe(cache, responses.Reader, canceled, typeUrl));
        }
        return new CacheCaseResult(fixture["id"]!.GetValue<string>(), typeUrl, ads, initial, requestVersion, names, wildcard, known, prefixes,
            events.ToArray(), observed.ToArray());
    }

    internal static void WriteResults(CacheCaseResult[] results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "cache-corpus.json"),
        JsonSerializer.Serialize(results, JsonOptions) + "\n");

    private static string[] Strings(JsonNode values) => values.AsArray().Select(value => value!.GetValue<string>()).ToArray();

    private static WireCacheSnapshot? ReadSnapshot(JsonNode? fixture, string typeUrl) => fixture is null ? null : new WireCacheSnapshot(
        fixture["version"]!.GetValue<string>(), fixture["omitGroup"]!.GetValue<bool>(),
        Strings(fixture["names"]!).Select(name => Convert.ToBase64String(Message(typeUrl, name).ToByteArray())).ToArray());

    private static Snapshot BuildSnapshot(WireCacheSnapshot snapshot, string typeUrl)
    {
        var groups = new Dictionary<string, IEnumerable<IMessage?>>();
        if (!snapshot.OmitGroup)
        {
            var parser = Message(typeUrl, "").Descriptor.Parser;
            groups[typeUrl] = snapshot.Data.Select(data => parser.ParseFrom(Convert.FromBase64String(data)));
        }
        return new Snapshot(snapshot.Version, groups);
    }

    private static IMessage Message(string typeUrl, string name) => typeUrl switch
    {
        ResourceTypeUrls.Listener => new Listener { Name = name },
        ResourceTypeUrls.Route => new RouteConfiguration { Name = name },
        ResourceTypeUrls.Cluster => new Cluster { Name = name },
        _ => throw new InvalidOperationException("Unsupported cache fixture family: " + typeUrl),
    };

    private static CacheObservation Observe(SnapshotCache cache, ChannelReader<ISotwResponse> responses, bool canceled, string typeUrl)
    {
        string? snapshotVersion;
        try
        {
            snapshotVersion = cache.GetSnapshot("node").GetVersion(typeUrl);
        }
        catch (KeyNotFoundException)
        {
            snapshotVersion = null;
        }
        CacheResponseObservation? response = null;
        string state = canceled ? "canceled" : "pending";
        if (responses.TryPeek(out var delivered))
        {
            state = "response";
            var parser = Message(typeUrl, "").Descriptor.Parser;
            string[] names = delivered.GetDiscoveryResponse().Resources.Select(payload => ResourceCatalog.GetName(parser.ParseFrom(payload.Value)))
                .Order(StringComparer.Ordinal).ToArray();
            response = new CacheResponseObservation(delivered.ResponseVersion, names,
                delivered.ReturnedResources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }
        return new CacheObservation(state, cache.GetStatusInfo("node")?.NumWatches, snapshotVersion, response);
    }
}

internal sealed record WireCacheSnapshot(string Version, bool OmitGroup, string[] Data);
internal sealed record WireCacheEvent(string Kind, WireCacheSnapshot? Snapshot);
internal sealed record CacheResponseObservation(string Version, string[] Names, Dictionary<string, string> Returned);
internal sealed record CacheObservation(string State, int? PendingWatchCount, string? SnapshotVersion, CacheResponseObservation? Response);
internal sealed record CacheCaseResult(string Id, string TypeUrl, bool Ads, WireCacheSnapshot? Initial, string RequestVersion, string[] Names,
    bool Wildcard, Dictionary<string, string> Known, string[] Prefixes, WireCacheEvent[] Events, CacheObservation[] Observed);
