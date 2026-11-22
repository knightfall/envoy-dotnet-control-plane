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
    internal const string RestCluster = "rest_api_cluster";
    internal const string ManagementHostname = "host.docker.internal";
    internal const string ManagementCaFile = "/etc/envoy/integration-ca.pem";
    internal const string ManagementClientCertificateFile = "/etc/envoy/integration-client.pem";
    internal const string ManagementClientKeyFile = "/etc/envoy/integration-client-key.pem";
    internal static readonly TimeSpan RestRefreshDelay = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan RestRequestTimeout = TimeSpan.FromSeconds(30);
    private const ushort AdminPort = 9901;
    private const string ConfigPath = "/etc/envoy/integration-proxy.yaml";
    private readonly IContainer container;
    private readonly string? sensitiveValue;
    private readonly HttpClient admin = new() { Timeout = TimeSpan.FromSeconds(5) };

    internal EnvoyProxyContainer(EnvoyImageLock images, INetwork network, string bootstrap, params int[] listenerPorts)
        : this(images, network, bootstrap, listenerPorts, listenerDrainSeconds: null)
    {
    }

    internal EnvoyProxyContainer(EnvoyImageLock images, INetwork network, string bootstrap,
        IReadOnlyList<int> listenerPorts, int? listenerDrainSeconds, IReadOnlyDictionary<string, byte[]>? files = null,
        string? sensitiveValue = null)
    {
        this.sensitiveValue = sensitiveValue;
        string[] command = ["envoy", "-c", ConfigPath, "--disable-hot-restart"];
        if (listenerDrainSeconds is { } seconds)
        {
            command = [.. command, "--drain-time-s", seconds.ToString(CultureInfo.InvariantCulture)];
        }
        if (files is not null)
        {
            command = [.. command, "--component-log-level", "config:debug"];
        }
        var builder = new ContainerBuilder(images.EnvoyIndex)
            .WithNetwork(network)
            .WithExtraHost(ManagementHostname, "host-gateway")
            .WithResourceMapping(Encoding.UTF8.GetBytes(bootstrap), FilePath.Of(ConfigPath))
            .WithCommand(command)
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
        if (files is not null)
        {
            foreach (var (path, content) in files)
            {
                builder = builder.WithResourceMapping(content, FilePath.Of(path));
            }
        }
        container = builder.Build();
    }

    internal Uri ListenerAddress(int port) => Address(port);

    internal async Task<string> GetErrorLogAsync(CancellationToken cancellationToken)
    {
        var (_, stderr) = await container.GetLogsAsync(ct: cancellationToken);
        return Redact(stderr);
    }

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
            if (sensitiveValue is { Length: > 0 } && error.ToString().Contains(sensitiveValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(Redact(error.ToString()));
            }
            throw;
        }
    }

    internal async Task<EnvoyConfigDump> GetConfigDumpAsync(CancellationToken cancellationToken, bool includeEndpoints = false) =>
        await admin.GetFromJsonAsync<EnvoyConfigDump>(
            new Uri(Address(AdminPort), includeEndpoints ? "/config_dump?include_eds" : "/config_dump"), cancellationToken)
        ?? throw new InvalidDataException("Envoy returned an empty config dump.");

    internal async Task<EnvoyRuntime> GetRuntimeAsync(CancellationToken cancellationToken) =>
        await admin.GetFromJsonAsync<EnvoyRuntime>(new Uri(Address(AdminPort), "/runtime"), cancellationToken)
        ?? throw new InvalidDataException("Envoy returned empty runtime state.");

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
                output.WriteLine($"Envoy stdout:\n{Redact(stdout)}\nEnvoy stderr:\n{Redact(stderr)}");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                output.WriteLine($"Envoy logs unavailable: {error.Message}");
            }
        }
        try
        {
            string dump = await admin.GetStringAsync(new Uri(Address(AdminPort), "/config_dump"));
            output.WriteLine($"Envoy /config_dump:\n{Redact(dump)}");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            output.WriteLine($"Envoy /config_dump unavailable: {error.Message}");
        }
    }

    private string Redact(string text) => string.IsNullOrEmpty(sensitiveValue)
        ? text
        : text.Replace(sensitiveValue, "[redacted]", StringComparison.Ordinal);

    internal static string RenderAdsBootstrap(string nodeId, int managementPort, bool delta = false) =>
        RenderBootstrap(nodeId, managementPort, RenderAdsResources(delta));

    internal static string RenderTlsAdsBootstrap(string nodeId, int managementPort, bool delta, bool sendClientCertificate = false,
        string? bearerToken = null)
    {
        string clientCertificate = sendClientCertificate ? $$"""
                      tls_certificates:
                      - certificate_chain: { filename: {{ManagementClientCertificateFile}} }
                        private_key: { filename: {{ManagementClientKeyFile}} }
            """ : "";
        string transportSocket = $$"""
                transport_socket:
                  name: envoy.transport_sockets.tls
                  typed_config:
                    "@type": type.googleapis.com/envoy.extensions.transport_sockets.tls.v3.UpstreamTlsContext
                    sni: {{ManagementHostname}}
                    common_tls_context:
                      alpn_protocols: [h2]
                      validation_context:
                        trusted_ca: { filename: {{ManagementCaFile}} }
                        match_typed_subject_alt_names:
                        - san_type: DNS
                          matcher: { exact: {{ManagementHostname}} }
            {{clientCertificate}}
            """;
        return RenderBootstrap(nodeId, managementPort, RenderAdsResources(delta, bearerToken, tls: true),
            transportSocket: transportSocket);
    }

    private static string RenderAdsResources(bool delta, string? bearerToken = null, bool tls = false)
    {
        // Envoy's native gRPC client lets its router derive :scheme from x-forwarded-proto.
        string metadata = tls ? """
                  initial_metadata:
                  - key: x-forwarded-proto
                    value: https
            """ : "";
        if (bearerToken is not null)
        {
            metadata += "\n      - key: authorization\n        value: " + JsonSerializer.Serialize("Bearer " + bearerToken);
        }
        string initialTimeout = tls ? ", initial_fetch_timeout: 1s" : "";
        return $$"""
            dynamic_resources:
              ads_config:
                api_type: {{(delta ? "DELTA_GRPC" : "GRPC")}}
                transport_api_version: V3
                set_node_on_first_message_only: true
                grpc_services:
                - envoy_grpc: { cluster_name: xds }
            {{metadata}}
              cds_config: { resource_api_version: V3, ads: {}{{initialTimeout}} }
              lds_config: { resource_api_version: V3, ads: {}{{initialTimeout}} }
            """;
    }

    internal static string RenderSplitBootstrap(string nodeId, int managementPort, bool delta = false) =>
        RenderBootstrap(nodeId, managementPort, $$"""
            dynamic_resources:
              cds_config: &grpc_source
                resource_api_version: V3
                api_config_source:
                  api_type: {{(delta ? "DELTA_GRPC" : "GRPC")}}
                  transport_api_version: V3
                  set_node_on_first_message_only: true
                  grpc_services:
                  - envoy_grpc: { cluster_name: xds }
              lds_config: *grpc_source
            """);

    internal static string RenderRestBootstrap(string nodeId, int managementPort) =>
        RenderBootstrap(nodeId, managementPort, string.Create(CultureInfo.InvariantCulture, $$"""
            dynamic_resources:
              cds_config: &rest_source
                resource_api_version: V3
                api_config_source:
                  api_type: REST
                  transport_api_version: V3
                  cluster_names: [{{RestCluster}}]
                  refresh_delay: {{RestRefreshDelay.TotalSeconds}}s
                  request_timeout: {{RestRequestTimeout.TotalSeconds}}s
              lds_config: *rest_source
            """), rest: true);

    private static string RenderBootstrap(string nodeId, int managementPort, string dynamicResources, bool rest = false,
        string transportSocket = "")
    {
        string managementCluster = rest ? RestCluster : "xds";
        string protocolOptions = rest ? "http_protocol_options" : "http2_protocol_options";
        return string.Create(CultureInfo.InvariantCulture, $$"""
            admin:
              address:
                socket_address: { address: 0.0.0.0, port_value: {{AdminPort}} }
            node:
              id: {{JsonSerializer.Serialize(nodeId)}}
              cluster: integration
            {{dynamicResources}}
            static_resources:
              clusters:
              - name: {{managementCluster}}
                type: STRICT_DNS
                dns_lookup_family: V4_ONLY
                connect_timeout: 1s
                typed_extension_protocol_options:
                  envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
                    "@type": type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
                    explicit_http_config: { {{protocolOptions}}: {} }
            {{transportSocket}}
                load_assignment:
                  cluster_name: {{managementCluster}}
                  endpoints:
                  - lb_endpoints:
                    - endpoint:
                        address:
                          socket_address: { address: {{ManagementHostname}}, port_value: {{managementPort}} }
            """);
    }
}

internal sealed record EnvoyConfigDump(IReadOnlyList<EnvoyConfig> Configs);

internal sealed record EnvoyConfig([property: JsonPropertyName("@type")] string Type)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record EnvoyStats(IReadOnlyList<EnvoyStat> Stats);

internal sealed record EnvoyRuntime(IReadOnlyDictionary<string, EnvoyRuntimeEntry> Entries);

internal sealed record EnvoyRuntimeEntry([property: JsonPropertyName("final_value")] string FinalValue);

internal sealed record EnvoyStat(string? Name, JsonElement Value)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = new(StringComparer.Ordinal);
}
