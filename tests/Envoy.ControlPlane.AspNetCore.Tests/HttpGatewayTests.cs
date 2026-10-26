using System.Net;
using System.Text;
using System.Text.Json;
using Envoy.Config.Core.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.HttpGatewayFixtures;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class HttpGatewayTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("denied", HttpStatusCode.Forbidden)]
    [InlineData("allowed", HttpStatusCode.OK)]
    public async Task Authorization_UsesOrdinaryEndpointConventions(string? credential, HttpStatusCode expected)
    {
        await using var host = await StartAsync(authorize: true,
            mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway().RequireAuthorization("xds"));
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "authorized"));
        using var client = Client(host);
        if (credential is not null) client.DefaultRequestHeaders.Add("authorization", "Bearer " + credential);
        using var body = new StringContent("""{"node":{"id":"node"}}""");
        using var response = await client.PostAsync("/v3/discovery:clusters", body, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK) Assert.False(host.Callbacks.FetchContexts.Reader.TryRead(out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Fact]
    public async Task SourceCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = HttpGatewayCorpus.Cases();
        Assert.Equal(23, inputs.Length);
        Assert.Equal(23, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<GatewayResult>();
        foreach (var input in inputs) results.Add(await HttpGatewayCorpus.EvaluateAsync(input));
        HttpGatewayCorpus.Write(results);
    }

    [Theory]
    [InlineData("clusters", ResourceTypeUrls.Cluster)]
    [InlineData("endpoints", ResourceTypeUrls.Endpoint)]
    [InlineData("listeners", ResourceTypeUrls.Listener)]
    [InlineData("routes", ResourceTypeUrls.Route)]
    [InlineData("scoped-routes", ResourceTypeUrls.ScopedRoute)]
    [InlineData("secrets", ResourceTypeUrls.Secret)]
    [InlineData("runtime", ResourceTypeUrls.Runtime)]
    [InlineData("extension_configs", ResourceTypeUrls.ExtensionConfig)]
    public async Task EightRoutes_OverrideBodyTypeAndUseProtobufJson(string suffix, string typeUrl)
    {
        await using var host = await StartAsync(mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway());
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(typeUrl, "json/v1"));
        using var client = Client(host);
        using var body = new StringContent("""{"node":{"id":"node"},"typeUrl":"ignored/type"}""", Encoding.UTF8, "text/plain");
        using var response = await client.PostAsync("/v3/discovery:" + suffix, body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("json/v1", parsed.RootElement.GetProperty("version_info").GetString());
        Assert.Equal(typeUrl, parsed.RootElement.GetProperty("type_url").GetString());
        Assert.Equal(typeUrl, Assert.Single(parsed.RootElement.GetProperty("resources").EnumerateArray()).GetProperty("@type").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("versionInfo", out _));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
        var context = await Wait(host.Callbacks.FetchContexts.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.IsAssignableFrom<HttpContext>(context.State);
    }

    [Theory]
    [InlineData("/v3//./discovery:clusters/")]
    [InlineData("/unused/../v3/discovery:clusters")]
    [InlineData("/../../v3/discovery:clusters")]
    public async Task CleanedAliases_SelectTheSameEndpoint(string path)
    {
        await using var host = await StartAsync(mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway());
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "cleaned"));
        var context = await InvokeAsync(host, path, """{"node":{"id":"node"}}""");
        await using var output = context.Response.Body;
        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(output.Length > 0);
    }

    [Theory]
    [InlineData("/v3/discovery:clusters", "", 400)]
    [InlineData("/v3/discovery:clusters", "not json", 400)]
    [InlineData("/v3/discovery:clusters", """{"unknown":1}""", 400)]
    [InlineData("/v3/discovery:clusters", """{"resource_names":123}""", 400)]
    [InlineData("/v3/discovery:clusters", """{"node":{"id":"missing"}}""", 500)]
    [InlineData("/v3/discovery:rls_configs", "{}", 404)]
    [InlineData("/v3/discovery:virtual_hosts", "{}", 404)]
    [InlineData("/v3/discovery:locality_endpoints", "{}", 404)]
    [InlineData("/v3/discovery:ads", "{}", 404)]
    public async Task Errors_KeepSourceStatusesAndDoNotLeakExceptionBodies(string path, string body, int expected)
    {
        await using var host = await StartAsync(mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway());
        var context = await InvokeAsync(host, path, body);
        await using var output = context.Response.Body;
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal(0, output.Length);
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedOrWrappedSkip_ProducesEmpty304(bool wrapped)
    {
        await using var host = await StartAsync(configure: (_, hooks) =>
        {
            if (wrapped) hooks.FetchRequestHandler = (_, _) => throw new InvalidOperationException("wrapper", new SkipFetchException());
        }, mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway());
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "v1"));
        using var client = Client(host);
        using var body = new StringContent("""{"node":{"id":"node"},"version_info":"v1"}""");
        using var response = await client.PostAsync("/v3/discovery:clusters", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(host.Cache.Inner.GetStatusKeys());
    }

    [Fact]
    public async Task BodyReadFailure_Is400BeforeCacheOrCallbacks()
    {
        await using var host = await StartAsync(mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway());
        var context = await InvokeAsync(host, "/v3/discovery:clusters", "", readFailure: true);
        await using var output = context.Response.Body;
        Assert.Equal(400, context.Response.StatusCode);
        Assert.False(host.Callbacks.FetchContexts.Reader.TryRead(out _));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task RegisteredExtensionAny_RoundTripsAndMissingRegistryFailsFormatting()
    {
        var listener = new Listener { Name = "listener" };
        listener.FilterChains.Add(new FilterChain
        {
            Filters = { new Filter { Name = "http", TypedConfig = Any.Pack(new HttpConnectionManager { StatPrefix = "http" }) } },
        });
        var snapshot = new Snapshot("nested", new Dictionary<string, IEnumerable<IMessage?>> { [ResourceTypeUrls.Listener] = [listener] });
        foreach (bool registered in new[] { false, true })
        {
            var registry = registered ? TypeRegistry.FromMessages(Listener.Descriptor, HttpConnectionManager.Descriptor) : null;
            await using var host = await StartAsync(mapEndpoints: app => app.MapEnvoyControlPlaneHttpGateway(registry));
            await host.Cache.Inner.SetSnapshotAsync("node", snapshot);
            var context = await InvokeAsync(host, "/v3/discovery:listeners", """{"node":{"id":"node"}}""");
            await using var output = context.Response.Body;
            Assert.Equal(registered ? 200 : 500, context.Response.StatusCode);
            if (registered)
            {
                output.Position = 0;
                using var reader = new StreamReader(output);
                string json = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                var decoded = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(registry!)).Parse<DiscoveryResponse>(json);
                Assert.Equal(listener, Assert.Single(decoded.Resources).Unpack<Listener>());
                Assert.Contains("stat_prefix", json, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(0, output.Length);
            }
        }
    }

    [Fact]
    public async Task Mapping_IsOptIn_AllowsOtherMethodsAndKeepsApplicationRoutesFirst()
    {
        await using (var disabled = await StartAsync())
        {
            using var client = Client(disabled);
            using var body = new StringContent("{}");
            using var response = await client.PostAsync("/v3/discovery:clusters", body, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        await using var host = await StartAsync(mapEndpoints: app =>
        {
            app.MapEnvoyControlPlaneHttpGateway();
            app.MapGet("/health", () => "healthy");
        });
        await host.Cache.Inner.SetSnapshotAsync("node", Snapshot(ResourceTypeUrls.Cluster, "method"));
        using var http = Client(host);
        using var health = await http.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal("healthy", await health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v3/discovery:clusters")
        {
            Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent("""{"node":{"id":"node"}}"""),
        };
        using var result = await http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
