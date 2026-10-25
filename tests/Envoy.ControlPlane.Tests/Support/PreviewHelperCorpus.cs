using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Example;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ListenerFilter = Envoy.Config.Listener.V3.Filter;

namespace Envoy.ControlPlane.Tests.Support;

internal static class PreviewHelperCorpus
{
    internal static readonly string[] GraphTypes = [ResourceTypeUrls.Cluster, ResourceTypeUrls.Endpoint, ResourceTypeUrls.Route, ResourceTypeUrls.Listener];

    internal static JsonArray ReadFixtures()
    {
        using var source = typeof(PreviewHelperCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.PreviewHelperCases.json")!;
        return JsonNode.Parse(source)!.AsArray();
    }

    internal static PreviewHelperCase Evaluate(JsonNode fixture)
    {
        ListenerFilter? filter = null;
        if (fixture["nullFilter"]?.GetValue<bool>() != true)
        {
            filter = new ListenerFilter { Name = fixture["filterName"]?.GetValue<string>() ?? "" };
            if (fixture["typeUrl"] is { } typeUrl)
            {
                filter.TypedConfig = new Any
                {
                    TypeUrl = typeUrl.GetValue<string>(),
                    Value = fixture["message"] is { } message
                        ? HttpConnectionManager.Parser.ParseJson(message.ToJsonString()).ToByteString()
                        : ByteString.FromBase64(fixture["value"]!.GetValue<string>()),
                };
            }
        }
        var manager = ResourceReferences.GetHttpConnectionManager(filter);
        return new PreviewHelperCase(fixture["id"]!.GetValue<string>(), filter is null ? null : Convert.ToBase64String(filter.ToByteArray()),
            manager is not null, manager?.Rds?.RouteConfigName);
    }

    internal static PreviewHelperReport Report(PreviewHelperCase[] cases)
    {
        const string version = "preview/v1";
        var snapshot = ExampleResources.CreateSnapshot(version, 8080, 10000);
        var names = GraphTypes.ToDictionary(type => type, type => snapshot.GetResources(type)!.Keys.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var groups = GraphTypes.Select(type => new WireGroup(type, snapshot.GetResources(type)!.Values
            .Select(resource => new WireEntry(Convert.ToBase64String(resource!.ToByteArray()), null)).ToArray())).ToArray();
        var ttls = GraphTypes.ToDictionary(type => type, type => names[type].ToDictionary(name => name, _ => (long?)null, StringComparer.Ordinal), StringComparer.Ordinal);
        var graph = new SnapshotCaseResult("preview-http-graph", version, groups, true, names,
            GraphTypes.ToDictionary(type => type, snapshot.GetVersion, StringComparer.Ordinal), ttls);
        var entries = GraphTypes.SelectMany(type => snapshot.GetResourcesAndTtl(type)!.Values);
        var references = ResourceReferences.GetReferences(entries).ToDictionary(pair => pair.Key,
            pair => pair.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return new PreviewHelperReport(new Dictionary<string, string>
        {
            ["httpConnectionManager"] = WellKnownFilterNames.HttpConnectionManager,
            ["router"] = WellKnownFilterNames.Router,
        }, (int)ApiVersion.V3, cases, graph, references);
    }

    internal static void Write(PreviewHelperReport report) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-helper-corpus.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}

internal sealed record PreviewHelperCase(string Id, string? FilterData, bool IsHcm, string? RouteName);
internal sealed record PreviewHelperReport(Dictionary<string, string> Labels, int ApiVersion, PreviewHelperCase[] Cases,
    SnapshotCaseResult Graph, Dictionary<string, string[]> References);
