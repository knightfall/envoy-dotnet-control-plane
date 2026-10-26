using System.Threading.Channels;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

public sealed partial class SnapshotCache
{
    /// <inheritdoc/>
    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
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
            info.LastDeltaWatch = timeProvider.GetUtcNow();
            if (snapshots.TryGetValue(nodeId, out var snapshot))
            {
                snapshot.ConstructVersionMap();
                bool replyIfEmpty = subscription.IsWildcard && request.ResponseNonce.Length == 0;
                if (DeltaResponseSelection.Create(snapshot, request, subscription, replyIfEmpty, default) is { } response)
                {
                    var immediate = new ResponseWatchRegistration<IDeltaResponse>(gate, responses, null);
                    _ = immediate.DeliverAsync(response, default);
                    return immediate;
                }
            }
            long watchId = ++nextWatchId;
            var registration = new ResponseWatchRegistration<IDeltaResponse>(gate, responses, () => RemoveDeltaWatch(nodeId, watchId));
            info.DeltaWatches.Add(watchId, new PendingDeltaWatch(request, subscription, registration));
            return registration;
        }
    }

    private void StartDeltaDeliveries(IResourceSnapshot snapshot, StatusInfo info, ResponseContext context, List<Task<bool>> deliveries)
    {
        if (info.DeltaWatches.Count == 0)
        {
            return;
        }
        snapshot.ConstructVersionMap();
        IEnumerable<PendingDeltaWatch> pending = info.DeltaWatches.Values;
        if (ads)
        {
            pending = pending.OrderBy(watch => ResourceCatalog.GetResponseType(watch.Request.TypeUrl));
        }
        foreach (var watch in pending.ToArray())
        {
            if (DeltaResponseSelection.Create(snapshot, watch.Request, watch.Subscription, replyIfEmpty: false, context) is { } response)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                deliveries.Add(watch.Registration.DeliverAsync(response, context.CancellationToken));
            }
        }
    }

    private void RemoveDeltaWatch(string nodeId, long watchId)
    {
        lock (gate)
        {
            if (status.TryGetValue(nodeId, out var info))
            {
                info.DeltaWatches.Remove(watchId);
            }
        }
    }

    private sealed record PendingDeltaWatch(DeltaDiscoveryRequest Request, ISubscription Subscription, ResponseWatchRegistration<IDeltaResponse> Registration);
}
