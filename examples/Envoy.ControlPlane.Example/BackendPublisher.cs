using Envoy.ControlPlane.Cache;

namespace Envoy.ControlPlane.Example;

internal sealed class BackendPublisher(SnapshotCache cache, IConfiguration configuration, ILogger<BackendPublisher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string nodeId = configuration["NodeId"] ?? "node";
        (int BackendPort, int ListenerPort, string BackendAddress, string ListenerAddress)? previous = null;
        long revision = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            int port = configuration.GetValue("BackendPort", 8080);
            int listenerPort = configuration.GetValue("ListenerPort", 10000);
            string backendAddress = configuration["BackendAddress"] ?? ExampleResources.LoopbackAddress;
            string listenerAddress = configuration["ListenerAddress"] ?? ExampleResources.LoopbackAddress;
            if (port is < 1 or > 65535)
            {
                throw new InvalidOperationException("BackendPort must be between 1 and 65535.");
            }
            if (listenerPort is < 1 or > 65535)
            {
                throw new InvalidOperationException("ListenerPort must be between 1 and 65535.");
            }
            var current = (port, listenerPort, backendAddress, listenerAddress);
            if (current != previous)
            {
                string version = "example/" + (++revision).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var snapshot = ExampleResources.CreateSnapshot(version, (uint)port, (uint)listenerPort, backendAddress, listenerAddress);
                await cache.SetSnapshotAsync(nodeId, snapshot, new ResponseContext(stoppingToken));
                previous = current;
                logger.LogInformation("Published snapshot version {Version} for node {NodeId}", version, nodeId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

}
