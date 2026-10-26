using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Envoy.Admin.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.EnvoyProbe;

internal static class EnvoyScenario
{
    private static readonly JsonParser AdminParser = new(JsonParser.Settings.Default.WithTypeRegistry(
        TypeRegistry.FromFiles(ConfigDumpReflection.Descriptor, ConfigDumpSharedReflection.Descriptor, RouteConfiguration.Descriptor.File)));

    internal static async Task RunAsync(string binary, string template, string output, bool ordered)
    {
        string directory = Path.Combine(output, ordered ? "ordered" : "unordered");
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var cache = new SnapshotCache();
        var callbacks = new ProbeCallbacks();
        var checks = new List<object>();
        await using var first = await ProbeHost.BackendAsync("A", token);
        await using var second = await ProbeHost.BackendAsync("B", token);
        await using var initialManagement = await ProbeHost.ManagementAsync(cache, callbacks, ordered, 0, token);
        ProbeHost? management = initialManagement;
        int managementPort = management.Port;
        var (listenerPort, adminPort) = UnusedPorts();
        string proxy = $"http://127.0.0.1:{listenerPort}/hello";
        string admin = $"http://127.0.0.1:{adminPort}";
        string bootstrap = Path.Combine(directory, "bootstrap.yaml");
        string contents = (await File.ReadAllTextAsync(template, token))
            .Replace("@ADMIN_PORT@", adminPort.ToString(), StringComparison.Ordinal)
            .Replace("@MANAGEMENT_PORT@", managementPort.ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(bootstrap, contents, token);
        var versions = ProbeResources.Types.ToDictionary(type => type, _ => "initial", StringComparer.Ordinal);
        await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, first.Port, listenerPort));
        await using var envoy = new EnvoyProcess(binary, bootstrap, directory);
        try
        {
            await TrafficAsync("A:/hello");
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            Require(callbacks.Requests.All(request => request.Node == "node"), "Envoy node identity was not retained");
            Check("bootstrap-and-initial-traffic", new { body = "A:/hello", connections = callbacks.Connections });

            versions[ResourceTypeUrls.Endpoint] = "endpoint-update";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort));
            await TrafficAsync("B:/hello");
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            Check("endpoint-update", "B:/hello");

            versions[ResourceTypeUrls.Route] = "route-update";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort, "/changed/"));
            await TrafficAsync("B:/changed/hello");
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            await RouteStateAsync("route-update", "/changed/", "route-update");
            Check("route-update", "B:/changed/hello");

            versions[ResourceTypeUrls.Route] = "invalid-route";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort, "/rejected/", invalidRoute: true));
            var nack = await callbacks.Nack.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            Require(nack.Type == ResourceTypeUrls.Route && nack.AcceptedVersion == "route-update"
                && nack.Nonce == nack.Response.Nonce && nack.Response.Version == "invalid-route" && nack.Error.Length > 0,
                "NACK did not retain the accepted version and reference the latest rejected response");
            Check("nack-association", nack);
            await TrafficAsync("B:/changed/hello");
            await RouteStateAsync("route-update", "/changed/", "after-nack");
            Require(cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Route) == "invalid-route", "Cache automatically rolled back after NACK");
            Check("nack-retains-accepted-config", "route-update");

            versions[ResourceTypeUrls.Route] = "repaired-route";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, second.Port, listenerPort, "/repaired/"));
            callbacks.ReleaseNack();
            await TrafficAsync("B:/repaired/hello");
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            await RouteStateAsync("repaired-route", "/repaired/", "repaired");
            Check("explicit-repair", "B:/repaired/hello");

            int connection = callbacks.Connections;
            await management.DisposeAsync();
            management = null;
            Require(callbacks.Closes == connection, "Management shutdown retained an open stream");
            Require(cache.GetStatusInfo("node")!.NumWatches == 0, "Management shutdown retained watches");
            await TrafficAsync("B:/repaired/hello");
            Check("disconnect-retains-traffic", "B:/repaired/hello");
            management = await ProbeHost.ManagementAsync(cache, callbacks, ordered, managementPort, token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.Connections > connection && callbacks.HasAcks(versions)), token);
            foreach (var pair in versions)
            {
                Require(callbacks.Requests.Any(request => request.Connection > connection && request.Type == pair.Key
                    && request.Version == pair.Value && request.Nonce.Length == 0), "Reconnect omitted an accepted version or reused an old nonce");
            }
            Check("reconnect-resubscribes", new { before = connection, after = callbacks.Connections });

            versions[ResourceTypeUrls.Endpoint] = "after-reconnect";
            await cache.SetSnapshotAsync("node", ProbeResources.Create(versions, first.Port, listenerPort, "/repaired/"));
            await TrafficAsync("A:/repaired/hello");
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            await RouteStateAsync("repaired-route", "/repaired/", "reconnected");
            Check("post-reconnect-update", "A:/repaired/hello");

            using var stopped = await http.PostAsync(admin + "/quitquitquit", null, token);
            stopped.EnsureSuccessStatusCode();
            await envoy.WaitForExitAsync(token);
            Require(envoy.ExitCode == 0, "Envoy did not exit successfully after its admin shutdown");
            await WaitUntilAsync(() => Task.FromResult(callbacks.Closes == callbacks.Connections), token);
            Require(cache.GetStatusInfo("node")!.NumWatches == 0, "Envoy exit retained watches");
            Check("cleanup", new { connections = callbacks.Connections, closes = callbacks.Closes, watches = 0 });
            await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
            {
                ordered,
                checks,
                requests = callbacks.Requests.ToArray(),
                responses = callbacks.Responses.Values.ToArray(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n", token);
        }
        finally
        {
            callbacks.ReleaseNack();
            if (management is not null) await management.DisposeAsync();
        }

        void Check(string name, object observed)
        {
            checks.Add(new { name, passed = true, observed });
            Console.WriteLine($"{(ordered ? "ordered" : "unordered")}: {name} passed");
        }

        async Task TrafficAsync(string expected)
        {
            await WaitUntilAsync(async () =>
            {
                envoy.EnsureRunning();
                try
                {
                    using var response = await http.GetAsync(proxy, token);
                    return response.IsSuccessStatusCode && await response.Content.ReadAsStringAsync(token) == expected;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
            }, token);
        }

        async Task RouteStateAsync(string version, string rewrite, string name)
        {
            string json = await http.GetStringAsync(admin + "/config_dump?resource=dynamic_route_configs", token);
            await File.WriteAllTextAsync(Path.Combine(directory, name + "-routes.json"), json, token);
            var dump = AdminParser.Parse<ConfigDump>(json);
            var active = dump.Configs.Select(value => value.Unpack<RoutesConfigDump.Types.DynamicRouteConfig>())
                .Single(value => value.RouteConfig.Unpack<RouteConfiguration>().Name == "routes");
            var route = active.RouteConfig.Unpack<RouteConfiguration>();
            Require(active.VersionInfo == version && route.VirtualHosts.Single().Domains.SequenceEqual(["*"])
                && route.VirtualHosts.Single().Routes.Single().Route_.PrefixRewrite == rewrite,
                "Envoy's accepted route configuration differs from expected traffic/config state");
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!await predicate()) await Task.Delay(25, timeout.Token);
    }

    private static (int Listener, int Admin) UnusedPorts()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var admin = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        admin.Start();
        return (((IPEndPoint)listener.LocalEndpoint).Port, ((IPEndPoint)admin.LocalEndpoint).Port);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
