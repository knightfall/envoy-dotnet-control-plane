using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.EnvoyProbe;

internal static class ProbeChecks
{
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static async Task WaitUntilAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!await predicate()) await Task.Delay(25, timeout.Token);
    }

    internal static (int Listener, int Admin) UnusedPorts()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var admin = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        admin.Start();
        return (((IPEndPoint)listener.LocalEndpoint).Port, ((IPEndPoint)admin.LocalEndpoint).Port);
    }

    internal static async Task TrafficAsync(HttpClient http, EnvoyProcess envoy, string url,
        HttpStatusCode status, string? body, CancellationToken token)
    {
        await WaitUntilAsync(async () =>
        {
            envoy.EnsureRunning();
            try
            {
                using var response = await http.GetAsync(url, token);
                return response.StatusCode == status && (body is null || await response.Content.ReadAsStringAsync(token) == body);
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }, token);
    }

    internal static async Task ClusterStateAsync(HttpClient http, string admin, string directory, string stage,
        bool present, CancellationToken token)
    {
        await WaitUntilAsync(async () =>
        {
            string json = await http.GetStringAsync(admin + "/config_dump?resource=dynamic_active_clusters", token);
            await File.WriteAllTextAsync(Path.Combine(directory, stage + "-clusters.json"), json, token);
            var dump = JsonNode.Parse(json)!.AsObject();
            bool found = dump["configs"]?.AsArray().Any(entry => entry!["cluster"]?["name"]?.GetValue<string>() == "service") ?? false;
            return found == present;
        }, token);
    }

    internal static async Task<long> RemovedClustersAsync(HttpClient http, string admin, string directory,
        string stage, CancellationToken token)
    {
        string json = await http.GetStringAsync(admin + "/stats?format=json", token);
        await File.WriteAllTextAsync(Path.Combine(directory, stage + "-stats.json"), json, token);
        var stats = JsonNode.Parse(json)!["stats"]!.AsArray();
        var counter = stats.Single(row => row?["name"]?.GetValue<string>() == "cluster_manager.cluster_removed");
        return counter!["value"]!.GetValue<long>();
    }
}
