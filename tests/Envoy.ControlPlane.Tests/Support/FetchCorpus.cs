using System.Text.Json;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests.Support;

internal static class FetchCorpus
{
    internal static FetchInput[] Cases() =>
    [
        new("all") { ExpectedNames = ["a", "b"] },
        new("exact") { Names = ["a"], ExpectedNames = ["a"] },
        new("duplicates") { Names = ["b", "a", "missing", "b"], ExpectedNames = ["b", "a", "b"] },
        new("literal-wildcard") { Names = ["*"] },
        new("literal-prefix") { Names = ["collection/*"] },
        new("missing-name") { Names = ["missing"] },
        new("same-version") { Version = "v1", ExpectedError = "skip" },
        new("missing-snapshot") { MissingSnapshot = true, ExpectedError = "missing" },
        new("unknown-type-current") { TypeUrl = "unknown/type", ExpectedError = "skip" },
        new("unknown-type-changed") { TypeUrl = "unknown/type", Version = "old", ExpectedVersion = "" },
        new("full-state-still-exact") { GroupType = ResourceTypeUrls.Cluster, TypeUrl = ResourceTypeUrls.Cluster, Names = ["a"], ExpectedNames = ["a"] },
        new("ads-still-exact") { Ads = true, Names = ["b"], ExpectedNames = ["b"] },
        new("ttl") { Names = ["a"], TtlNanoseconds = 5_000_000_000, ExpectedNames = ["a"] },
        new("zero-ttl") { Names = ["a"], TtlNanoseconds = 0, ExpectedNames = ["a"] },
    ];

    internal static async Task<FetchResult> EvaluateAsync(FetchInput input)
    {
        var cache = new SnapshotCache(input.Ads);
        if (!input.MissingSnapshot)
        {
            IMessage first = input.GroupType == ResourceTypeUrls.Cluster ? new Cluster { Name = "a" } : new RouteConfiguration { Name = "a" };
            IMessage second = input.GroupType == ResourceTypeUrls.Cluster ? new Cluster { Name = "b" } : new RouteConfiguration { Name = "b" };
            await cache.SetSnapshotAsync("node", new Snapshot("v1", new Dictionary<string, IEnumerable<ResourceEntry>>
            {
                [input.GroupType] = [new(first, input.TtlNanoseconds is { } ns ? TimeSpan.FromTicks(ns / 100) : null), new(second)],
            }));
        }
        string error = "";
        string version = "";
        string typeUrl = "";
        string[] payloads = [];
        try
        {
            var raw = await cache.FetchAsync(Request(input.TypeUrl, input.Version, names: input.Names));
            Assert.Empty(raw.ReturnedResources);
            var response = raw.GetDiscoveryResponse();
            version = response.VersionInfo;
            typeUrl = response.TypeUrl;
            payloads = response.Resources.Select(resource => Convert.ToBase64String(resource.ToByteArray())).ToArray();
            if (input.Names.Length == 0) Array.Sort(payloads, StringComparer.Ordinal);
            string[] names = response.Resources.Select(resource => Name(resource, input.GroupType)).ToArray();
            if (input.Names.Length == 0) Array.Sort(names, StringComparer.Ordinal);
            Assert.Equal(input.ExpectedNames, names);
            Assert.Equal(input.ExpectedVersion, version);
        }
        catch (SkipFetchException) { error = "skip"; }
        catch (KeyNotFoundException) { error = "missing"; }
        Assert.Equal(input.ExpectedError, error);
        Assert.Empty(cache.GetStatusKeys());
        return new FetchResult(input, new FetchObservation(error, version, typeUrl, payloads));
    }

    internal static void Write(IEnumerable<FetchResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fetch-corpus.json"),
        JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

    private static string Name(Google.Protobuf.WellKnownTypes.Any resource, string typeUrl)
    {
        if (resource.Is(Envoy.Service.Discovery.V3.Resource.Descriptor))
        {
            return resource.Unpack<Envoy.Service.Discovery.V3.Resource>().Name;
        }
        return typeUrl == ResourceTypeUrls.Cluster ? resource.Unpack<Cluster>().Name : resource.Unpack<RouteConfiguration>().Name;
    }
}

internal sealed record FetchInput(string Id)
{
    public string TypeUrl { get; init; } = ResourceTypeUrls.Route;
    public string GroupType { get; init; } = ResourceTypeUrls.Route;
    public string[] Names { get; init; } = [];
    public string Version { get; init; } = "";
    public bool Ads { get; init; }
    public bool MissingSnapshot { get; init; }
    public long? TtlNanoseconds { get; init; }
    public string ExpectedError { get; init; } = "";
    public string ExpectedVersion { get; init; } = "v1";
    public string[] ExpectedNames { get; init; } = [];
}

internal sealed record FetchObservation(string Error, string Version, string TypeUrl, string[] Payloads);
internal sealed record FetchResult(FetchInput Input, FetchObservation Observed);
