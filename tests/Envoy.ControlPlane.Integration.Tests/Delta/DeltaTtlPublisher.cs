using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;

namespace Envoy.ControlPlane.Integration.Tests.Delta;

internal sealed class DeltaTtlPublisher(SnapshotCache cache, string nodeId, string clusterName, TimeSpan ttl) : IConfigWatcher
{
    private HeartbeatWatch? current;

    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses) =>
        cache.CreateWatch(request, subscription, responses);

    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
    {
        if (request.TypeUrl != ResourceTypeUrls.Cluster)
        {
            return cache.CreateDeltaWatch(request, subscription, responses);
        }

        var watch = cache.CreateDeltaWatch(request, subscription, new TtlWriter(responses, cache, nodeId));
        if (subscription.ReturnedResources.TryGetValue(clusterName, out string? version))
        {
            Volatile.Write(ref current, new HeartbeatWatch(request, version, responses));
        }
        return watch;
    }

    internal async Task RunHeartbeatsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Volatile.Read(ref current) is not { } watch)
                {
                    continue;
                }
                var response = new DeltaDiscoveryResponse
                {
                    TypeUrl = ResourceTypeUrls.Cluster,
                    SystemVersionInfo = cache.GetSnapshot(nodeId).GetVersion(ResourceTypeUrls.Cluster),
                    Resources =
                    {
                        new Resource { Name = clusterName, Version = watch.Version, Ttl = Duration.FromTimeSpan(ttl) },
                    },
                };
                await watch.Responses.WriteAsync(new PassthroughDeltaResponse(watch.Request, response,
                    new Dictionary<string, string>(StringComparer.Ordinal) { [clusterName] = watch.Version }), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The test cancels and awaits the publisher before inspecting expiry or disposing the host.
        }
    }

    private sealed record HeartbeatWatch(DeltaDiscoveryRequest Request, string Version, ChannelWriter<IDeltaResponse> Responses);

    private sealed class TtlWriter(ChannelWriter<IDeltaResponse> destination, SnapshotCache cache, string nodeId) : ChannelWriter<IDeltaResponse>
    {
        public override bool TryComplete(Exception? error = null) => false;
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            destination.WaitToWriteAsync(cancellationToken);
        public override bool TryWrite(IDeltaResponse item) => destination.TryWrite(WithTtl(item));
        public override ValueTask WriteAsync(IDeltaResponse item, CancellationToken cancellationToken = default) =>
            destination.WriteAsync(WithTtl(item), cancellationToken);

        private PassthroughDeltaResponse WithTtl(IDeltaResponse response)
        {
            var envelope = response.GetDeltaDiscoveryResponse().Clone();
            var entries = cache.GetSnapshot(nodeId).GetResourcesAndTtl(ResourceTypeUrls.Cluster);
            foreach (var resource in envelope.Resources)
            {
                if (entries?.GetValueOrDefault(resource.Name)?.Ttl is { } currentTtl)
                {
                    resource.Ttl = Duration.FromTimeSpan(currentTtl);
                }
            }
            return new PassthroughDeltaResponse(response.Request, envelope, response.ReturnedResources, response.Context);
        }
    }
}
