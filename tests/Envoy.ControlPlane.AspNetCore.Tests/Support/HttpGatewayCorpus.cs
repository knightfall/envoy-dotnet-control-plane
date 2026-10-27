using System.Text.Json;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.HttpGatewayFixtures;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal static class HttpGatewayCorpus
{
    internal static GatewayInput[] Cases() =>
    [
        new("success") { ExpectedStatus = 200 },
        new("type-override") { Body = """{"node":{"id":"node"},"typeUrl":"ignored"}""", ExpectedStatus = 200 },
        new("clean-path") { Path = "/v3//unused/.././discovery:clusters/", ExpectedStatus = 200 },
        new("empty-body") { Body = "", ExpectedStatus = 400 },
        new("invalid-json") { Body = "invalid", ExpectedStatus = 400 },
        new("unreadable-body") { ReadFailure = true, ExpectedStatus = 400 },
        new("unknown-field") { Body = """{"unknown":1}""", ExpectedStatus = 400 },
        new("wrong-field-type") { Body = """{"resource_names":false}""", ExpectedStatus = 400 },
        new("missing-snapshot") { Body = """{"node":{"id":"missing"}}""", ExpectedStatus = 500 },
        new("skip") { Body = """{"node":{"id":"node"},"version_info":"v1"}""", ExpectedStatus = 304 },
        new("wrapped-skip") { CallbackError = "skip", ExpectedStatus = 304 },
        new("callback-error") { CallbackError = "reject", ExpectedStatus = 500 },
        new("unknown-path") { Path = "/other", ExpectedStatus = 404 },
        new("no-ratelimit-route") { Path = "/v3/discovery:rls_configs", ExpectedStatus = 404 },
        new("ttl") { TtlNanoseconds = 5_000_000_000, ExpectedStatus = 200 },
        new("zero-ttl") { TtlNanoseconds = 0, ExpectedStatus = 200 },
        new("unknown-any") { BrokenAny = true, ExpectedStatus = 500 },
        new("malformed-any") { MalformedAny = true, ExpectedStatus = 500 },
        new("enum-named") { Body = """{"node":{"id":"node","listening_addresses":[{"socket_address":{"protocol":"UDP","port_value":8080}}]}}""", ExpectedStatus = 200 },
        new("enum-numeric") { Body = """{"node":{"id":"node","listening_addresses":[{"socket_address":{"protocol":1,"port_value":"8080"}}]}}""", ExpectedStatus = 200 },
        new("invalid-enum") { Body = """{"node":{"id":"node","listening_addresses":[{"socket_address":{"protocol":"not-an-enum"}}]}}""", ExpectedStatus = 400 },
        new("struct-and-presence") { Body = """{"node":{"id":"node","user_agent_version":"","metadata":{"enabled":true,"number":1.5,"list":[null,"value"]}}}""", ExpectedStatus = 200 },
        new("null-fields") { Body = """{"node":{"id":"node","metadata":null},"resource_names":null}""", ExpectedStatus = 200 },
        new("route-endpoints") { Path = ResourceFetchPaths.Endpoints, TypeUrl = ResourceTypeUrls.Endpoint, ExpectedStatus = 200 },
        new("route-listeners") { Path = ResourceFetchPaths.Listeners, TypeUrl = ResourceTypeUrls.Listener, ExpectedStatus = 200 },
        new("route-routes") { Path = ResourceFetchPaths.Routes, TypeUrl = ResourceTypeUrls.Route, ExpectedStatus = 200 },
        new("route-scoped-routes") { Path = ResourceFetchPaths.ScopedRoutes, TypeUrl = ResourceTypeUrls.ScopedRoute, ExpectedStatus = 200 },
        new("route-secrets") { Path = ResourceFetchPaths.Secrets, TypeUrl = ResourceTypeUrls.Secret, ExpectedStatus = 200 },
        new("route-runtime") { Path = ResourceFetchPaths.Runtime, TypeUrl = ResourceTypeUrls.Runtime, ExpectedStatus = 200 },
        new("route-extension-configs") { Path = ResourceFetchPaths.ExtensionConfigs, TypeUrl = ResourceTypeUrls.ExtensionConfig, ExpectedStatus = 200 },
    ];

    internal static async Task<GatewayResult> EvaluateAsync(GatewayInput input)
    {
        JsonElement? parsedRequest = null;
        await using var host = await StartAsync(configure: (_, hooks) =>
        {
            hooks.FetchRequestHandler = (_, request) =>
            {
                parsedRequest = JsonSerializer.Deserialize<JsonElement>(new JsonFormatter(JsonFormatter.Settings.Default.WithPreserveProtoFieldNames(true)).Format(request));
                if (input.CallbackError == "skip") throw new InvalidOperationException("wrapped", new Cache.SkipFetchException());
                if (input.CallbackError == "reject") throw new InvalidOperationException("fixture callback rejection");
                return ValueTask.CompletedTask;
            };
            if (input.BrokenAny || input.MalformedAny)
            {
                hooks.FetchResponseHandler = response => response!.Resources.Add(new Any
                {
                    TypeUrl = input.MalformedAny ? ResourceTypeUrls.Cluster : "type.googleapis.com/unknown.Type",
                    Value = input.MalformedAny ? ByteString.CopyFrom([0xff]) : ByteString.Empty,
                });
            }
        }, mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway());
        IMessage resource = new Cluster
        {
            Name = "a", ConnectTimeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(3)), Type = Cluster.Types.DiscoveryType.StrictDns,
            Metadata = new Metadata { FilterMetadata = { ["settings"] = new Struct { Fields = { ["enabled"] = Value.ForBool(true), ["number"] = Value.ForNumber(1.5) } } } },
        };
        if (input.TypeUrl != ResourceTypeUrls.Cluster)
        {
            resource = DiscoveryCalls.Message(input.TypeUrl, "a");
        }
        await host.Cache.Inner.SetSnapshotAsync("node", new Snapshot("v1", new Dictionary<string, IEnumerable<ResourceEntry>>
        {
            [input.TypeUrl] = [new(resource, input.TtlNanoseconds is { } ns ? TimeSpan.FromTicks(ns / 100) : null)],
        }));
        var context = await InvokeAsync(host, input.Path, input.Body, input.ReadFailure);
        await using var output = context.Response.Body;
        Assert.Equal(input.ExpectedStatus, context.Response.StatusCode);
        JsonElement? json = null;
        if (context.Response.StatusCode == 200)
        {
            output.Position = 0;
            json = await JsonSerializer.DeserializeAsync<JsonElement>(output, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("v1", json.Value.GetProperty("version_info").GetString());
            Assert.Equal(input.TypeUrl, json.Value.GetProperty("type_url").GetString());
        }
        else
        {
            Assert.Equal(0, output.Length);
        }
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
        return new GatewayResult(input, new GatewayObservation(context.Response.StatusCode, json, parsedRequest));
    }

    internal static void Write(IEnumerable<GatewayResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gateway-corpus.json"),
        JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

internal sealed record GatewayInput(string Id)
{
    public string TypeUrl { get; init; } = ResourceTypeUrls.Cluster;
    public string Path { get; init; } = "/v3/discovery:clusters";
    public string Body { get; init; } = """{"node":{"id":"node"}}""";
    public bool ReadFailure { get; init; }
    public string CallbackError { get; init; } = "";
    public bool BrokenAny { get; init; }
    public bool MalformedAny { get; init; }
    public long? TtlNanoseconds { get; init; }
    public int ExpectedStatus { get; init; }
}

internal sealed record GatewayObservation(int Status, JsonElement? Json, JsonElement? Request);
internal sealed record GatewayResult(GatewayInput Input, GatewayObservation Observed);
