using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using static Envoy.ControlPlane.EnvoyProbe.ProbeChecks;

namespace Envoy.ControlPlane.EnvoyProbe;

internal static class TtlEnvoyScenario
{
    internal static async Task RunAsync(string binary, string template, string output)
    {
        string directory = Path.Combine(output, "ttl");
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var cache = new SnapshotCache();
        var callbacks = new DeltaProbeCallbacks();
        var observations = new ConcurrentQueue<TtlObservation>();
        callbacks.ResponseObserved = Observe;
        var checks = new List<object>();
        var ttl = TimeSpan.FromSeconds(3);
        var watcher = new TtlDeltaWatcher(cache, ttl);
        await using var backend = await ProbeHost.BackendAsync("TTL", token);
        await using var management = await ProbeHost.ManagementAsync(watcher, callbacks, 0, token);
        var (listenerPort, adminPort) = UnusedPorts();
        string proxy = $"http://127.0.0.1:{listenerPort}/hello";
        string admin = $"http://127.0.0.1:{adminPort}";
        string bootstrap = Path.Combine(directory, "bootstrap.yaml");
        string contents = (await File.ReadAllTextAsync(template, token))
            .Replace("api_type: GRPC", "api_type: DELTA_GRPC", StringComparison.Ordinal)
            .Replace("@ADMIN_PORT@", adminPort.ToString(), StringComparison.Ordinal)
            .Replace("@MANAGEMENT_PORT@", management.Port.ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(bootstrap, contents, token);
        var versions = ProbeResources.Types.ToDictionary(type => type, _ => "ttl-initial", StringComparer.Ordinal);
        var snapshot = ProbeResources.Create(versions, backend.Port, listenerPort, clusterTtl: ttl);
        await cache.SetSnapshotAsync("node", snapshot);
        using var stopHeartbeats = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task heartbeats = watcher.RunHeartbeatsAsync(stopHeartbeats.Token);
        await using var envoy = new EnvoyProcess(binary, bootstrap, directory);
        try
        {
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "TTL:/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(versions)), token);
            await ClusterStateAsync(http, admin, directory, "initial", present: true, token);
            long beforeRemoval = await RemovedClustersAsync(http, admin, directory, "initial", token);
            Check("ttl-initial-traffic", new { ttlSeconds = ttl.TotalSeconds, body = "TTL:/hello" });

            long firstPayload = observations.First(observed => observed.HasPayload).Timestamp;
            int successfulRequests = 0;
            while (Stopwatch.GetElapsedTime(firstPayload) < ttl * 2.5)
            {
                envoy.EnsureRunning();
                using var response = await http.GetAsync(proxy, token);
                Require(response.StatusCode == HttpStatusCode.OK && await response.Content.ReadAsStringAsync(token) == "TTL:/hello",
                    "TTL resource expired while payload-free refreshes were active");
                successfulRequests++;
                await Task.Delay(100, token);
            }
            await ClusterStateAsync(http, admin, directory, "refreshed", present: true, token);
            var refreshed = observations.ToArray();
            Require(refreshed.Count(observed => observed.HasPayload) == 1 && refreshed.Count(observed => !observed.HasPayload) >= 3,
                "TTL refresh did not use payload-free heartbeats after one full configuration");
            string resourceVersion = refreshed.Single(observed => observed.HasPayload).ResourceVersion;
            Require(resourceVersion.Length > 0 && refreshed.All(observed => observed.Name == "service"
                && observed.Ttl == ttl && observed.Version == "ttl-initial" && observed.ResourceVersion == resourceVersion),
                "TTL refresh changed the resource identity, TTL or configuration version");
            Check("ttl-refresh-without-config-resend", new
            {
                successfulRequests, elapsedSeconds = Stopwatch.GetElapsedTime(firstPayload).TotalSeconds,
                fullPayloads = 1, heartbeats = refreshed.Count(observed => !observed.HasPayload),
            });

            await stopHeartbeats.CancelAsync();
            await heartbeats;
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.ServiceUnavailable, null, token);
            await ClusterStateAsync(http, admin, directory, "expired", present: false, token);
            long afterRemoval = await RemovedClustersAsync(http, admin, directory, "expired", token);
            Require(afterRemoval > beforeRemoval, "Envoy did not increment its cluster removal counter after TTL expiry");
            Require(observations.Count(observed => observed.HasPayload) == 1, "Expiry was masked by a full configuration resend");
            Check("ttl-expiry-and-counter", new { beforeRemoval, afterRemoval, status = 503 });

            Require(ReferenceEquals(snapshot, cache.GetSnapshot("node"))
                && snapshot.GetResourcesAndTtl(ResourceTypeUrls.Cluster)!.Single().Value.Ttl == ttl,
                "Proxy TTL expiration was confused with cache deletion");
            Require(cache.GetStatusInfo("node")!.NumDeltaWatches > 0, "Stopping TTL refresh unexpectedly disposed the cache watches");
            Check("ttl-cache-retained", new { version = snapshot.GetVersion(ResourceTypeUrls.Cluster), resources = 1 });

            versions[ResourceTypeUrls.Cluster] = "after-ttl";
            var repaired = ProbeResources.Create(versions, backend.Port, listenerPort);
            var cluster = (Envoy.Config.Cluster.V3.Cluster)repaired.GetResources(ResourceTypeUrls.Cluster)!["service"]!;
            cluster.ConnectTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(TimeSpan.FromSeconds(2));
            await cache.SetSnapshotAsync("node", repaired);
            await TrafficAsync(http, envoy, proxy, HttpStatusCode.OK, "TTL:/hello", token);
            await WaitUntilAsync(() => Task.FromResult(callbacks.HasAcks(new Dictionary<string, string> { [ResourceTypeUrls.Cluster] = "after-ttl" })), token);
            await ClusterStateAsync(http, admin, directory, "repaired", present: true, token);
            Check("ttl-explicit-repair", "TTL:/hello");

            using var stopped = await http.PostAsync(admin + "/quitquitquit", null, token);
            stopped.EnsureSuccessStatusCode();
            await envoy.WaitForExitAsync(token);
            Require(envoy.ExitCode == 0, "TTL Envoy did not exit successfully");
            await WaitUntilAsync(() => Task.FromResult(callbacks.Closes == callbacks.Connections), token);
            Require(cache.GetStatusInfo("node")!.NumDeltaWatches == 0 && heartbeats.IsCompletedSuccessfully,
                "TTL cleanup retained a heartbeat task or stream watch");
            Require(callbacks.Requests.All(request => request.Error is null), "TTL scenario received a NACK");
            Check("ttl-cleanup", new { connections = callbacks.Connections, closes = callbacks.Closes, watches = 0 });
            await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
            {
                protocol = "delta-ttl", checks, observations = observations.ToArray(), requests = callbacks.Requests.ToArray(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n", token);
        }
        finally
        {
            await stopHeartbeats.CancelAsync();
            await heartbeats;
        }

        void Observe(DeltaDiscoveryResponse response)
        {
            if (response.TypeUrl != ResourceTypeUrls.Cluster) return;
            foreach (var resource in response.Resources)
            {
                observations.Enqueue(new TtlObservation(resource.Name, response.SystemVersionInfo, resource.Version, resource.Ttl?.ToTimeSpan(),
                    resource.Resource_ is not null, Stopwatch.GetTimestamp()));
            }
        }

        void Check(string name, object observed)
        {
            checks.Add(new { name, passed = true, observed });
            Console.WriteLine("ttl: " + name + " passed");
        }
    }

    private sealed record TtlObservation(string Name, string Version, string ResourceVersion, TimeSpan? Ttl, bool HasPayload, long Timestamp);
}
