using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal sealed class EnvoyProxyContainer : IAsyncDisposable
{
    private const ushort AdminPort = 9901;
    private const string ConfigPath = "/etc/envoy/integration-proxy.yaml";
    private readonly IContainer container;
    private readonly HttpClient admin = new() { Timeout = TimeSpan.FromSeconds(5) };

    internal EnvoyProxyContainer(EnvoyImageLock images, INetwork network, string bootstrap, params int[] listenerPorts)
    {
        var builder = new ContainerBuilder(images.EnvoyIndex)
            .WithNetwork(network)
            .WithExtraHost("host.docker.internal", "host-gateway")
            .WithResourceMapping(Encoding.UTF8.GetBytes(bootstrap), FilePath.Of(ConfigPath))
            .WithCommand("envoy", "-c", ConfigPath, "--disable-hot-restart")
            .WithPortBinding(AdminPort, assignRandomHostPort: true)
            .WithCreateParameterModifier(parameters =>
            {
                var ports = parameters.HostConfig?.PortBindings
                    ?? throw new InvalidOperationException("Envoy's admin port binding is missing.");
                foreach (var binding in ports.Values.SelectMany(bindings => bindings))
                {
                    binding.HostIP = "127.0.0.1";
                }
            })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(AdminPort).ForPath("/ready"), wait => wait.WithTimeout(TimeSpan.FromSeconds(45))));
        foreach (int port in listenerPorts)
        {
            builder = builder.WithPortBinding(port, assignRandomHostPort: true);
        }
        container = builder.Build();
    }

    internal Uri ListenerAddress(int port) => Address(port);

    internal Task StopAsync(CancellationToken cancellationToken) => container.StopAsync(cancellationToken);

    internal async Task RunWithDiagnosticsAsync(ITestOutputHelper output, Func<Task> test, CancellationToken cancellationToken)
    {
        try
        {
            await container.StartAsync(cancellationToken);
            await test();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await CaptureDiagnosticsAsync(output);
            throw;
        }
    }

    internal async Task<EnvoyConfigDump> GetConfigDumpAsync(CancellationToken cancellationToken) =>
        await admin.GetFromJsonAsync<EnvoyConfigDump>(new Uri(Address(AdminPort), "/config_dump"), cancellationToken)
        ?? throw new InvalidDataException("Envoy returned an empty config dump.");

    internal async Task<EnvoyStats> GetStatsAsync(CancellationToken cancellationToken, string? filter = null)
    {
        string path = filter is null ? "/stats?format=json" : "/stats?format=json&filter=" + Uri.EscapeDataString(filter);
        return await admin.GetFromJsonAsync<EnvoyStats>(new Uri(Address(AdminPort), path), cancellationToken)
            ?? throw new InvalidDataException("Envoy returned an empty stats response.");
    }

    public async ValueTask DisposeAsync()
    {
        admin.Dispose();
        await container.DisposeAsync();
    }

    private Uri Address(int containerPort) =>
        new UriBuilder(Uri.UriSchemeHttp, container.Hostname, container.GetMappedPublicPort(containerPort)).Uri;

    private async Task CaptureDiagnosticsAsync(ITestOutputHelper output)
    {
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                var (stdout, stderr) = await container.GetLogsAsync(ct: timeout.Token);
                output.WriteLine($"Envoy stdout:\n{stdout}\nEnvoy stderr:\n{stderr}");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                output.WriteLine($"Envoy logs unavailable: {error.Message}");
            }
        }
        try
        {
            string dump = await admin.GetStringAsync(new Uri(Address(AdminPort), "/config_dump"));
            output.WriteLine($"Envoy /config_dump:\n{dump}");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            output.WriteLine($"Envoy /config_dump unavailable: {error.Message}");
        }
    }

    internal static string RenderAdsBootstrap(string nodeId, int managementPort, bool delta = false) =>
        string.Create(CultureInfo.InvariantCulture, $$"""
            admin:
              address:
                socket_address: { address: 0.0.0.0, port_value: {{AdminPort}} }
            node:
              id: {{JsonSerializer.Serialize(nodeId)}}
              cluster: integration
            dynamic_resources:
              ads_config:
                api_type: {{(delta ? "DELTA_GRPC" : "GRPC")}}
                transport_api_version: V3
                set_node_on_first_message_only: true
                grpc_services:
                - envoy_grpc: { cluster_name: xds }
              cds_config: { resource_api_version: V3, ads: {} }
              lds_config: { resource_api_version: V3, ads: {} }
            static_resources:
              clusters:
              - name: xds
                type: STRICT_DNS
                dns_lookup_family: V4_ONLY
                connect_timeout: 1s
                typed_extension_protocol_options:
                  envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
                    "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
                    explicit_http_config: { http2_protocol_options: {} }
                load_assignment:
                  cluster_name: xds
                  endpoints:
                  - lb_endpoints:
                    - endpoint:
                        address:
                          socket_address: { address: host.docker.internal, port_value: {{managementPort}} }
            """);
}

internal sealed record EnvoyConfigDump(IReadOnlyList<EnvoyConfig> Configs);

internal sealed record EnvoyConfig([property: JsonPropertyName("@type")] string Type)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record EnvoyStats(IReadOnlyList<EnvoyStat> Stats);

internal sealed record EnvoyStat(string? Name, JsonElement Value)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = new(StringComparer.Ordinal);
}
