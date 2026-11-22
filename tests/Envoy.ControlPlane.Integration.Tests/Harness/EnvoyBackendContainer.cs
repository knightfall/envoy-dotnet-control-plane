using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal sealed class EnvoyBackendContainer : IAsyncDisposable
{
    internal const int Port = 8080;
    private const string ConfigPath = "/etc/envoy/integration-backend.yaml";
    private readonly IContainer container;

    internal EnvoyBackendContainer(EnvoyImageLock images, INetwork network, string name)
    {
        container = new ContainerBuilder(images.EnvoyIndex)
            .WithNetwork(network)
            .WithResourceMapping(Encoding.UTF8.GetBytes(RenderConfig(name)), FilePath.Of(ConfigPath))
            .WithCommand("envoy", "-c", ConfigPath, "--disable-hot-restart")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("starting main dispatch loop", wait => wait.WithTimeout(TimeSpan.FromSeconds(30))))
            .Build();
    }

    internal IPAddress Address
    {
        get
        {
            // This container is attached only to the shared test network.
            if (IPAddress.TryParse(container.IpAddress, out var address) && address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address;
            }
            throw new InvalidOperationException("The Envoy backend has no IPv4 address on the test network.");
        }
    }

    internal Task StartAsync(CancellationToken cancellationToken) => container.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => container.DisposeAsync();

    private static string RenderConfig(string name) => string.Create(CultureInfo.InvariantCulture, $$"""
        static_resources:
          listeners:
          - name: backend
            address:
              socket_address: { address: 0.0.0.0, port_value: {{Port}} }
            filter_chains:
            - filters:
              - name: envoy.filters.network.http_connection_manager
                typed_config:
                  "@type": type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                  stat_prefix: backend
                  route_config:
                    name: backend
                    virtual_hosts:
                    - name: backend
                      domains: ["*"]
                      routes:
                      - match: { prefix: "/" }
                        direct_response:
                          status: 200
                          body:
                            inline_string: {{JsonSerializer.Serialize(name)}}
                          body_format:
                            text_format_source:
                              inline_string: "%LOCAL_REPLY_BODY%:%REQ(:PATH)%"
                  http_filters:
                  - name: envoy.filters.http.router
                    typed_config:
                      "@type": type.googleapis.com/envoy.extensions.filters.http.router.v3.Router
        """);
}
