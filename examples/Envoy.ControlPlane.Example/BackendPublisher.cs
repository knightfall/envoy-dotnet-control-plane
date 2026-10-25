using Envoy.ControlPlane.Cache;

namespace Envoy.ControlPlane.Example;

internal sealed class BackendPublisher(SnapshotCache cache, IConfiguration configuration, ILogger<BackendPublisher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string nodeId = configuration["NodeId"] ?? "node";
        int previousPort = 0;
        int previousListenerPort = 0;
        long revision = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            int port = configuration.GetValue("BackendPort", 8080);
            int listenerPort = configuration.GetValue("ListenerPort", 10000);
            if (port is < 1 or > 65535)
            {
                throw new InvalidOperationException("BackendPort must be between 1 and 65535.");
            }
            if (listenerPort is < 1 or > 65535)
            {
                throw new InvalidOperationException("ListenerPort must be between 1 and 65535.");
            }
            if (port != previousPort || listenerPort != previousListenerPort)
            {
                string version = "example/" + (++revision).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var snapshot = ExampleResources.CreateSnapshot(version, (uint)port, (uint)listenerPort);
                await cache.SetSnapshotAsync(nodeId, snapshot, new ResponseContext(stoppingToken));
                previousPort = port;
                previousListenerPort = listenerPort;
                logger.LogInformation("Published snapshot version {Version} for node {NodeId}", version, nodeId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

}
