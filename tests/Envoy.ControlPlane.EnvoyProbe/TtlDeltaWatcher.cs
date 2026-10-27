using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;

namespace Envoy.ControlPlane.EnvoyProbe;

/// <summary>Supplies caller-built Delta TTL envelopes; the source SnapshotCache deliberately omits Delta TTL.</summary>
internal sealed class TtlDeltaWatcher(SnapshotCache cache, TimeSpan ttl) : IConfigWatcher
{
    private TtlWatch? current;

    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses) =>
        cache.CreateWatch(request, subscription, responses);

    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
    {
        if (request.TypeUrl != ResourceTypeUrls.Cluster) return cache.CreateDeltaWatch(request, subscription, responses);
        var writer = new TtlWriter(responses, ttl);
        var owned = cache.CreateDeltaWatch(request, subscription, writer);
        var watch = new TtlWatch(owned, request,
            new Dictionary<string, string>(subscription.ReturnedResources, StringComparer.Ordinal), responses);
        Volatile.Write(ref current, watch);
        return watch;
    }

    internal async Task RunHeartbeatsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var watch = Volatile.Read(ref current);
                if (watch?.Disposed != false) continue;
                if (!watch.ReturnedResources.TryGetValue("service", out string? version)) continue;
                var response = new DeltaDiscoveryResponse
                {
                    TypeUrl = ResourceTypeUrls.Cluster,
                    SystemVersionInfo = cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Cluster),
                };
                response.Resources.Add(new Resource { Name = "service", Version = version, Ttl = Duration.FromTimeSpan(ttl) });
                await watch.Responses.WriteAsync(new PassthroughDeltaResponse(watch.Request, response,
                    watch.ReturnedResources), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // This probe owns and awaits the only publisher task before stopping its host.
        }
    }

    private sealed class TtlWatch(IDisposable owned, DeltaDiscoveryRequest request, IReadOnlyDictionary<string, string> returnedResources,
        ChannelWriter<IDeltaResponse> responses) : IDisposable, IAsyncDisposable
    {
        private int disposed;
        internal bool Disposed => Volatile.Read(ref disposed) != 0;
        internal DeltaDiscoveryRequest Request => request;
        internal IReadOnlyDictionary<string, string> ReturnedResources => returnedResources;
        internal ChannelWriter<IDeltaResponse> Responses => responses;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owned.Dispose();
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (owned is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
            else owned.Dispose();
        }
    }

    private sealed class TtlWriter(ChannelWriter<IDeltaResponse> destination, TimeSpan ttl) : ChannelWriter<IDeltaResponse>
    {
        public override bool TryComplete(Exception? error = null) => false;
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => destination.WaitToWriteAsync(cancellationToken);
        public override bool TryWrite(IDeltaResponse item) => destination.TryWrite(WithTtl(item));
        public override ValueTask WriteAsync(IDeltaResponse item, CancellationToken cancellationToken = default) =>
            destination.WriteAsync(WithTtl(item), cancellationToken);

        private PassthroughDeltaResponse WithTtl(IDeltaResponse response)
        {
            var envelope = response.GetDeltaDiscoveryResponse().Clone();
            foreach (var resource in envelope.Resources) resource.Ttl = Duration.FromTimeSpan(ttl);
            return new PassthroughDeltaResponse(response.Request, envelope, response.ReturnedResources, response.Context);
        }
    }
}
