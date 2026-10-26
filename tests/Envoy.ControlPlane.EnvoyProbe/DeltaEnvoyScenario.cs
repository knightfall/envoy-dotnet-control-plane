using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using static Envoy.ControlPlane.EnvoyProbe.ProbeChecks;

namespace Envoy.ControlPlane.EnvoyProbe;

internal static class DeltaEnvoyScenario
{
    internal static async Task RunAsync(string binary, string template, string output)
    {
        string directory = Path.Combine(output, "delta");
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var cache = new SnapshotCache();
        var callbacks = new DeltaProbeCallbacks();
        var checks = new List<object>();
        await using var first = await ProbeHost.BackendAsync("A", token);
        await using var second = await ProbeHost.BackendAsync("B", token);
        await using var initialManagement = await ProbeHost.ManagementAsync(cache, callbacks, 0, token);
        ProbeHost? management = initialManagement;
        int managementPort = management.Port;
        var (listenerPort, adminPort) = UnusedPorts();
        string proxy = $"http://127.0.0.1:{listenerPort}/hello";
        string admin = $"http://127.0.0.1:{adminPort}";
        string bootstrap = Path.Combine(directory, "bootstrap.yaml");
        string contents = (await File.ReadAllTextAsync(template, token))
            .Replace("api_type: GRPC", "api_type: DELTA_GRPC", StringComparison.Ordinal)
            .Replace("@ADMIN_PORT@", adminPort.ToString(), StringComparison.Ordinal)
            .Replace("@MANAGEMENT_PORT@", managementPort.ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(bootstrap, contents, token);
        var versions = ProbeResources.Types.ToDictionary(type => type, _ => "initial", StringComparer.Ordinal);
        await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, first.Port, listenerPort));
        await using var envoy = new EnvoyProcess(binary, bootstrap, directory);
        try
        {
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "A:/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            Require(callbacks.Requests.Any(request => request.Node == "node"), "Delta Envoy omitted its node identity");
            Require(callbacks.Requests.All(request => request.Error is null), "Initial Delta graph was NACKed");
            Check("delta-initial-traffic-and-types", new { types = ProbeResources.Types, body = "A:/hello" });

            versions[ResourceTypeUrls.Endpoint] = "endpoint-update";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort));
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "B:/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            await SaveEndpointsAsync("endpoint-update", second.Port);
            Check("delta-endpoint-update", "B:/hello");

            versions[ResourceTypeUrls.Route] = "route-update";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort, "/delta/"));
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "B:/delta/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            string routes = await http.GetStringAsync(admin + "/config_dump?resource=dynamic_route_configs", token);
            await File.WriteAllTextAsync(Path.Combine(directory, "route-update-routes.json"), routes, token);
            var route = JsonNode.Parse(routes)!["configs"]!.AsArray().Single()!["route_config"]!;
            Require(route["name"]!.GetValue<string>() == "routes"
                && route["virtual_hosts"]![0]!["routes"]![0]!["route"]!["prefix_rewrite"]!.GetValue<string>() == "/delta/",
                "Delta route admin state differs from traffic");
            Check("delta-route-update", "B:/delta/hello");

            versions[ResourceTypeUrls.Cluster] = "removed";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort, "/delta/", removeCluster: true));
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.ServiceUnavailable, null, token);
            await ClusterStateAsync(http, admin, directory, "removed", present: false, token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(new Dictionary<string, string> { [ResourceTypeUrls.Cluster] = "removed" })), token);
            var removed = callbacks.Responses[ResourceTypeUrls.Cluster];
            Require(removed.Resources.Count == 0 && removed.Removed.SequenceEqual(["service"]), "Delta removal omitted its exact tombstone");
            Check("delta-removal", removed);

            versions[ResourceTypeUrls.Cluster] = "restored";
            var accepted = ProbeResources.Create(versions, second.Port, listenerPort, "/delta/");
            await cache.SetSnapshotAsync("node", accepted);
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "B:/delta/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            await ClusterStateAsync(http, admin, directory, "restored", present: true, token);
            Check("delta-readd", "B:/delta/hello");

            int connection = callbacks.Connections;
            await management.DisposeAsync();
            management = null;
            Require(callbacks.Closes == connection && cache.GetStatusInfo("node")!.NumDeltaWatches == 0,
                "Delta management shutdown retained a stream or watch");
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "B:/delta/hello", token);
            Check("delta-disconnect-retains-traffic", "B:/delta/hello");
            management = await ProbeHost.ManagementAsync(cache, callbacks, managementPort, token);
            accepted.ConstructVersionMap();
            await WaitUntilAsync(() => Task.FromResult(callbacks.Connections > connection && ProbeResources.Types.All(type =>
                callbacks.Requests.Any(request => request.Connection > connection && request.Type == type && request.Nonce.Length == 0))), token);
            foreach (string type in ProbeResources.Types)
            {
                var request = callbacks.Requests.First(request => request.Connection > connection && request.Type == type && request.Nonce.Length == 0);
                var expected = accepted.GetVersionMap(type)!;
                Require(request.InitialVersions.Count == expected.Count && expected.All(pair => request.InitialVersions.GetValueOrDefault(pair.Key) == pair.Value),
                    "Delta reconnect did not echo accepted per-resource versions for " + type);
            }
            Check("delta-reconnect-initial-versions", new { before = connection, after = callbacks.Connections });

            versions[ResourceTypeUrls.Endpoint] = "after-reconnect";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, first.Port, listenerPort, "/delta/"));
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "A:/delta/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(new Dictionary<string, string> { [ResourceTypeUrls.Endpoint] = "after-reconnect" })), token);
            await SaveEndpointsAsync("reconnected", first.Port);
            Check("delta-post-reconnect-update", "A:/delta/hello");

            using var stopped = await http.PostAsync(admin + "/quitquitquit", null, token);
            stopped.EnsureSuccessStatusCode();
            await envoy.WaitForExitAsync(token);
            Require(envoy.ExitCode == 0, "Delta Envoy did not exit successfully");
            await WaitUntilAsync(() => Task.FromResult(callbacks.Closes == callbacks.Connections), token);
            Require(cache.GetStatusInfo("node")!.NumDeltaWatches == 0, "Delta Envoy exit retained watches");
            Require(callbacks.Requests.All(request => request.Error is null), "Delta graph received a NACK");
            Check("delta-cleanup", new { connections = callbacks.Connections, closes = callbacks.Closes, watches = 0 });
            await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
            {
                protocol = "delta", checks, requests = callbacks.Requests.ToArray(), responses = callbacks.History.ToArray(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n", token);
        }
        finally
        {
            if (management is not null) await management.DisposeAsync();
        }

        void Check(string name, object observed)
        {
            checks.Add(new { name, passed = true, observed });
            Console.WriteLine("delta: " + name + " passed");
        }

        async Task SaveEndpointsAsync(string stage, int port)
        {
            string json = await http.GetStringAsync(admin + "/config_dump?resource=dynamic_endpoint_configs&include_eds", token);
            await File.WriteAllTextAsync(Path.Combine(directory, stage + "-endpoints.json"), json, token);
            var entries = JsonNode.Parse(json)!["configs"]!.AsArray();
            Require(entries.Any(entry => entry!["endpoint_config"]?["cluster_name"]?.GetValue<string>() == "service"
                && entry["endpoint_config"]!["endpoints"]![0]!["lb_endpoints"]![0]!["endpoint"]!["address"]!["socket_address"]!["port_value"]!.GetValue<int>() == port),
                "Delta EDS admin state differs from the selected backend");
        }
    }
}
