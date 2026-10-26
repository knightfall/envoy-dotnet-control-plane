using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Stores stable snapshots and delivers one-shot SotW and Delta responses by node.</summary>
public sealed partial class SnapshotCache : IConfigWatcher, IConfigFetcher
{
    private readonly object gate = new();
    private readonly SemaphoreSlim publication = new(1, 1);
    private readonly Dictionary<string, IResourceSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StatusInfo> status = new(StringComparer.Ordinal);
    private readonly bool ads;
    private readonly INodeHasher nodeHasher;
    private readonly TimeProvider timeProvider;
    private long nextWatchId;

    /// <summary>Creates a cache with optional ADS readiness, node hashing and application clock.</summary>
    /// <param name="ads">Whether exact-name subscriptions must cover snapshot resources.</param>
    /// <param name="nodeHasher">The node-key mapper; the default uses node IDs.</param>
    /// <param name="timeProvider">The clock for status request times; the default uses system UTC time.</param>
    public SnapshotCache(bool ads = false, INodeHasher? nodeHasher = null, TimeProvider? timeProvider = null)
    {
        this.ads = ads;
        this.nodeHasher = nodeHasher ?? new IdNodeHasher();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Publishes a stable snapshot and completes eligible watches without awaiting proxy ACK.</summary>
    /// <param name="nodeId">The cache key for the snapshot.</param>
    /// <param name="snapshot">The replacement snapshot, retained without validation or cloning.</param>
    /// <param name="context">The publication context forwarded to delivered responses.</param>
    /// <remarks>Cancellation of a queued publication prevents installation; cancellation after it begins can leave its snapshot installed.</remarks>
    public async Task SetSnapshotAsync(string nodeId, IResourceSnapshot snapshot, ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Keep the source's install-before-cancellation behavior once publication can begin.
        if (!publication.Wait(0))
        {
            await publication.WaitAsync(context.CancellationToken);
        }
        try
        {
            var deliveries = new List<Task<bool>>();
            Exception? failure = null;
            try
            {
                lock (gate)
                {
                    snapshots[nodeId] = snapshot;
                    if (status.TryGetValue(nodeId, out var info))
                    {
                        StartDeliveries(snapshot, info, context, deliveries);
                        StartDeltaDeliveries(snapshot, info, context, deliveries);
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failure = error;
            }
            try
            {
                await Task.WhenAll(deliveries);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failure ??= error;
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            publication.Release();
        }
    }

    private void StartDeliveries(IResourceSnapshot snapshot, StatusInfo info, ResponseContext context,
        List<Task<bool>> deliveries)
    {
        IEnumerable<PendingWatch> pending = info.Watches.Values;
        if (ads)
        {
            pending = pending.OrderBy(watch => ResourceCatalog.GetResponseType(watch.Request.TypeUrl));
        }
        // Begin writes in publication order; await buffer space only after releasing cache synchronization.
        foreach (var watch in pending.ToArray())
        {
            if (snapshot.GetVersion(watch.Request.TypeUrl) != watch.Request.VersionInfo)
            {
                var selection = SotwResponseSelection.Create(snapshot, watch.Request, watch.Subscription, ads, context);
                if (selection.Response is { } response)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    deliveries.Add(watch.Registration.DeliverAsync(response, context.CancellationToken));
                }
            }
        }
    }

    /// <summary>Returns the retained snapshot for a node.</summary>
    /// <param name="nodeId">The cache key.</param>
    /// <exception cref="KeyNotFoundException">The node has no published snapshot.</exception>
    public IResourceSnapshot GetSnapshot(string nodeId)
    {
        lock (gate)
        {
            return snapshots.TryGetValue(nodeId, out var snapshot) ? snapshot
                : throw new KeyNotFoundException("No snapshot found for node " + nodeId);
        }
    }

    /// <summary>Removes snapshot and indexed status without completing or canceling old watches.</summary>
    /// <param name="nodeId">The cache key.</param>
    public void ClearSnapshot(string nodeId)
    {
        lock (gate)
        {
            snapshots.Remove(nodeId);
            status.Remove(nodeId);
        }
    }

    /// <inheritdoc/>
    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(responses);
        string nodeId = nodeHasher.Hash(request.Node);
        lock (gate)
        {
            if (!status.TryGetValue(nodeId, out var info))
            {
                info = new StatusInfo(gate, request.Node);
                status[nodeId] = info;
            }
            info.LastWatch = timeProvider.GetUtcNow();
            if (snapshots.TryGetValue(nodeId, out var snapshot))
            {
                var selection = SotwResponseSelection.Create(snapshot, request, subscription, ads, default);
                if (selection.Untracked)
                {
                    return new ResponseWatchRegistration<ISotwResponse>(gate, responses, null);
                }
                if (selection.Response is { } response)
                {
                    var immediate = new ResponseWatchRegistration<ISotwResponse>(gate, responses, null);
                    _ = immediate.DeliverAsync(response, default);
                    return immediate;
                }
            }
            long watchId = ++nextWatchId;
            var completion = new ResponseWatchRegistration<ISotwResponse>(gate, responses, () => RemoveWatch(nodeId, watchId));
            info.Watches.Add(watchId, new PendingWatch(request, subscription, completion));
            return completion;
        }
    }

    /// <summary>Returns the live synchronized status view, or null when no status is indexed.</summary>
    /// <param name="nodeId">The cache key.</param>
    public IStatusInfo? GetStatusInfo(string nodeId)
    {
        lock (gate)
        {
            return status.GetValueOrDefault(nodeId);
        }
    }

    /// <summary>Returns current status keys; publication alone does not create status.</summary>
    public string[] GetStatusKeys()
    {
        lock (gate)
        {
            return status.Keys.ToArray();
        }
    }

    private void RemoveWatch(string nodeId, long watchId)
    {
        lock (gate)
        {
            if (status.TryGetValue(nodeId, out var info))
            {
                info.Watches.Remove(watchId);
            }
        }
    }

    private sealed record PendingWatch(DiscoveryRequest Request, ISubscription Subscription, ResponseWatchRegistration<ISotwResponse> Registration);

    private sealed class StatusInfo(object gate, Node? node) : IStatusInfo
    {
        internal Dictionary<long, PendingWatch> Watches { get; } = new();
        internal Dictionary<long, PendingDeltaWatch> DeltaWatches { get; } = new();
        internal DateTimeOffset LastWatch { get; set; }
        internal DateTimeOffset LastDeltaWatch { get; set; }
        public Node? Node
        {
            get
            {
                lock (gate)
                {
                    return node;
                }
            }
        }

        public int NumWatches
        {
            get
            {
                lock (gate)
                {
                    return Watches.Count;
                }
            }
        }

        public int NumDeltaWatches
        {
            get
            {
                lock (gate)
                {
                    return DeltaWatches.Count;
                }
            }
        }

        public DateTimeOffset LastDeltaWatchRequestTime
        {
            get
            {
                lock (gate)
                {
                    return LastDeltaWatch;
                }
            }
        }

        public DateTimeOffset LastWatchRequestTime
        {
            get
            {
                lock (gate)
                {
                    return LastWatch;
                }
            }
        }
    }
}
