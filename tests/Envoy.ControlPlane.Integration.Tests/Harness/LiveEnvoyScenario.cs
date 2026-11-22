using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal sealed class LiveEnvoyScenario(EnvoyImageLock images, INetwork network, SnapshotCache cache, HostControlPlaneFixture host,
    HttpClient http, string nodeId, CancellationToken cancellationToken)
{
    internal string NodeId { get; } = nodeId;
    internal static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(25);
    internal SnapshotCache Cache { get; } = cache;
    internal HostControlPlaneFixture Host { get; } = host;
    internal CancellationToken Token { get; } = cancellationToken;
    internal StreamObservations<DiscoveryRequest, DiscoveryResponse> Observations => Host.Observations.Sotw;

    internal static async Task RunAsync(Func<LiveEnvoyScenario, Task> scenario, bool ads = true, bool ordered = false,
        string nodeId = "sotw-integration", Func<SnapshotCache, IConfigWatcher>? wrapCache = null,
        Func<IConfigWatcher, HostControlPlaneFixture>? createHost = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var images = EnvoyImageLock.Load();
        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync(timeout.Token);
        var cache = new SnapshotCache(ads);
        var watcher = wrapCache?.Invoke(cache) ?? cache;
        await using var host = createHost?.Invoke(watcher) ?? new HostControlPlaneFixture(watcher,
            configureServices: services => services.AddSingleton(new SotwOptions { OrderedAds = ordered }));
        await host.StartAsync(timeout.Token);
        using var http = new HttpClient();
        // Every probe establishes a fresh connection, including probes through a TCP listener.
        http.DefaultRequestHeaders.ConnectionClose = true;
        await scenario(new LiveEnvoyScenario(images, network, cache, host, http, nodeId, timeout.Token));
    }

    internal EnvoyBackendContainer Backend(string name, bool http1Only = false) => new(images, network, name, http1Only);

    internal EnvoyProxyContainer Proxy(string bootstrap, params int[] ports) => new(images, network, bootstrap, ports);

    internal EnvoyProxyContainer Proxy(string bootstrap, IReadOnlyList<int> ports, int listenerDrainSeconds) =>
        new(images, network, bootstrap, ports, listenerDrainSeconds);

    internal EnvoyProxyContainer Proxy(string bootstrap, IReadOnlyDictionary<string, byte[]> files, string? sensitiveValue, params int[] ports) =>
        new(images, network, bootstrap, ports, listenerDrainSeconds: null, files, sensitiveValue);

    internal string AdsBootstrap(bool delta = false) => EnvoyProxyContainer.RenderAdsBootstrap(NodeId, Host.GrpcPort, delta);

    internal string SplitBootstrap(bool delta = false) => EnvoyProxyContainer.RenderSplitBootstrap(NodeId, Host.GrpcPort, delta);

    internal Task PublishAsync(Snapshot snapshot)
    {
        snapshot.ConstructVersionMap();
        return Cache.SetSnapshotAsync(NodeId, snapshot, new ResponseContext(Token));
    }

    internal Task UntilAsync(Func<bool> condition, string description) =>
        AsyncPoll.UntilAsync(condition, description, PollTimeout, Token);

    internal Task UntilAsync(Func<CancellationToken, Task<bool>> condition, string description) =>
        AsyncPoll.UntilAsync(condition, description, PollTimeout, Token);

    internal Task WaitForAcksAsync(Snapshot snapshot, IEnumerable<string> types) =>
        UntilAsync(() => types.All(type => HasAck(type, snapshot.GetVersion(type))), "SotW ACKs for the published snapshot");

    internal bool HasAck(string type, string version, long? streamId = null) =>
        Host.Observations.HasSotwAck(type, version, streamId);

    internal async Task ExpectTrafficAsync(EnvoyProxyContainer proxy, int port, string? expectedBody,
        HttpStatusCode status = HttpStatusCode.OK, string path = "/sotw", Action<HttpRequestMessage>? configureRequest = null)
    {
        string? lastObservation = null;
        try
        {
            await UntilAsync(async token =>
            {
                try
                {
                    var (actualStatus, body) = await ReadTrafficAsync(proxy, port, path, token, configureRequest);
                    lastObservation = $"{(int)actualStatus} {body}";
                    return actualStatus == status && (expectedBody is null || body == expectedBody);
                }
                catch (HttpRequestException error)
                {
                    lastObservation = error.Message;
                    return false;
                }
            }, $"HTTP {(int)status} and body '{expectedBody}' through container port {port}");
        }
        catch (TimeoutException error)
        {
            throw new TimeoutException($"{error.Message} Last result: {lastObservation}", error);
        }
    }

    internal async Task<(HttpStatusCode Status, string Body)> ReadTrafficAsync(EnvoyProxyContainer proxy, int port,
        string path, CancellationToken cancellationToken, Action<HttpRequestMessage>? configureRequest = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(proxy.ListenerAddress(port), path));
        configureRequest?.Invoke(request);
        using var response = await http.SendAsync(request, cancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    internal static IEnumerable<JsonElement> AdminEntries(EnvoyConfigDump dump, string type, string field) =>
        dump.Configs.Where(config => config.Type == "type.googleapis.com/envoy.admin.v3." + type
            && config.Fields.ContainsKey(field)).SelectMany(config => config.Fields[field].EnumerateArray());
}
