using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using EnvoyEndpoint = Envoy.Config.Endpoint.V3.Endpoint;

namespace Envoy.ControlPlane.Example;

internal sealed class BackendPublisher(SnapshotCache cache, IConfiguration configuration, ILogger<BackendPublisher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string nodeId = configuration["NodeId"] ?? "node";
        int previousPort = 0;
        long revision = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            int port = configuration.GetValue("BackendPort", 8080);
            if (port is < 1 or > 65535)
            {
                throw new InvalidOperationException("BackendPort must be between 1 and 65535.");
            }
            if (port != previousPort)
            {
                string version = "example/" + (++revision).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await cache.SetSnapshotAsync(nodeId, CreateSnapshot(version, (uint)port), new ResponseContext(stoppingToken));
                previousPort = port;
                logger.LogInformation("Published endpoint version {Version} for node {NodeId}", version, nodeId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static Snapshot CreateSnapshot(string version, uint port)
    {
        var assignment = new ClusterLoadAssignment
        {
            ClusterName = "service",
            Endpoints =
            {
                new LocalityLbEndpoints
                {
                    LbEndpoints =
                    {
                        new LbEndpoint
                        {
                            Endpoint = new EnvoyEndpoint
                            {
                                Address = new Address
                                {
                                    SocketAddress = new SocketAddress { Address = "127.0.0.1", PortValue = port },
                                },
                            },
                        },
                    },
                },
            },
        };
        return new Snapshot(version, new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Endpoint] = [assignment],
        });
    }
}
