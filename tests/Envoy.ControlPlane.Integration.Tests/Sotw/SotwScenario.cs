using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envoy.ControlPlane.Integration.Tests.Sotw;

internal sealed class SotwScenario(EnvoyImageLock images, INetwork network, SnapshotCache cache, HostControlPlaneFixture host,
    HttpClient http, CancellationToken cancellationToken)
{
    internal const string NodeId = "sotw-integration";
    internal static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(25);
    internal SnapshotCache Cache { get; } = cache;
    internal HostControlPlaneFixture Host { get; } = host;
    internal CancellationToken Token { get; } = cancellationToken;
    internal StreamObservations<DiscoveryRequest, DiscoveryResponse> Observations => Host.Observations.Sotw;

    internal static async Task RunAsync(Func<SotwScenario, Task> scenario, bool ads = true, bool ordered = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var images = EnvoyImageLock.Load();
        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync(timeout.Token);
        var cache = new SnapshotCache(ads);
        await using var host = new HostControlPlaneFixture(cache,
            configureServices: services => services.AddSingleton(new SotwOptions { OrderedAds = ordered }));
        await host.StartAsync(timeout.Token);
        using var http = new HttpClient();
        // Every probe establishes a fresh connection, including probes through a TCP listener.
        http.DefaultRequestHeaders.ConnectionClose = true;
        await scenario(new SotwScenario(images, network, cache, host, http, timeout.Token));
    }

    internal EnvoyBackendContainer Backend(string name, bool http1Only = false) => new(images, network, name, http1Only);

    internal EnvoyProxyContainer Proxy(string bootstrap, params int[] ports) => new(images, network, bootstrap, ports);

    internal string AdsBootstrap() => EnvoyProxyContainer.RenderAdsBootstrap(NodeId, Host.GrpcPort);

    internal string SplitBootstrap() => EnvoyProxyContainer.RenderSplitBootstrap(NodeId, Host.GrpcPort);

    internal Task PublishAsync(Snapshot snapshot) => Cache.SetSnapshotAsync(NodeId, snapshot, new ResponseContext(Token));

    internal Task UntilAsync(Func<bool> condition, string description) =>
        AsyncPoll.UntilAsync(condition, description, PollTimeout, Token);

    internal Task UntilAsync(Func<CancellationToken, Task<bool>> condition, string description) =>
        AsyncPoll.UntilAsync(condition, description, PollTimeout, Token);

    internal Task WaitForAcksAsync(Snapshot snapshot, IEnumerable<string> types) =>
        UntilAsync(() => types.All(type => HasAck(type, snapshot.GetVersion(type))), "SotW ACKs for the published snapshot");

    internal bool HasAck(string type, string version, long? streamId = null) =>
        Host.Observations.HasSotwAck(type, version, streamId);

    internal async Task ExpectTrafficAsync(EnvoyProxyContainer proxy, int port, string? expectedBody,
        HttpStatusCode status = HttpStatusCode.OK, bool scoped = false)
    {
        var uri = new Uri(proxy.ListenerAddress(port), "/sotw");
        string? lastObservation = null;
        try
        {
            await UntilAsync(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (scoped)
                {
                    request.Headers.Add(SotwResources.ScopeHeader, SotwResources.Scope);
                }
                try
                {
                    using var response = await http.SendAsync(request, token);
                    string body = await response.Content.ReadAsStringAsync(token);
                    lastObservation = $"{(int)response.StatusCode} {body}";
                    return response.StatusCode == status && (expectedBody is null || body == expectedBody);
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

    internal static IEnumerable<JsonElement> AdminEntries(EnvoyConfigDump dump, string type, string field) =>
        dump.Configs.Where(config => config.Type == "type.googleapis.com/envoy.admin.v3." + type
            && config.Fields.ContainsKey(field)).SelectMany(config => config.Fields[field].EnumerateArray());
}
