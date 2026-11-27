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
        IDeltaResponse? immediateResponse = null;
        StatusInfo? observedStatus = null;
        while (true)
        {
            Snapshot toPrepare;
            lock (gate)
            {
                if (!status.TryGetValue(nodeId, out var info) || !ReferenceEquals(info, observedStatus))
                {
                    var requestTime = timeProvider.GetUtcNow();
                    if (info is null)
                    {
                        info = new StatusInfo(gate, request.Node);
                        status[nodeId] = info;
                    }
                    info.LastDeltaWatch = requestTime;
                    observedStatus = info;
                }

                snapshots.TryGetValue(nodeId, out var snapshot);
                if (snapshot is Snapshot builtIn && !builtIn.HasDeltaResources)
                {
                    toPrepare = builtIn;
                }
                else
                {
                    if (snapshot is not null)
                    {
                        snapshot.ConstructVersionMap();
                        bool replyIfEmpty = subscription.IsWildcard && request.ResponseNonce.Length == 0;
                        immediateResponse = DeltaResponseSelection.Create(snapshot, request, subscription, replyIfEmpty, default);
                    }
                    if (immediateResponse is null)
                    {
                        long watchId = ++nextWatchId;
                        var registration = new ResponseWatchRegistration<IDeltaResponse>(gate, responses, () => RemoveDeltaWatch(nodeId, watchId));
                        info.DeltaWatches.Add(watchId, new PendingDeltaWatch(request, subscription, registration));
                        return registration;
                    }
                    break;
                }
            }
            // The node's snapshot can change during serialization. Recheck it before selecting or registering.
            toPrepare.ConstructVersionMap();
        }
        var immediate = new ResponseWatchRegistration<IDeltaResponse>(gate, responses, null);
        _ = immediate.DeliverAsync(immediateResponse, default);
        return immediate;
    }

    private void SelectDeltaDeliveries(IResourceSnapshot snapshot, StatusInfo info, ResponseContext context, List<Func<Task<bool>>> deliveries)
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
        var shared = snapshot is Snapshot && info.DeltaWatches.Count > 1
            ? new DeltaResponseSelection.WildcardSelections() : null;
        foreach (var watch in pending.ToArray())
        {
            if (DeltaResponseSelection.Create(snapshot, watch.Request, watch.Subscription, replyIfEmpty: false, context, shared) is { } response)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                deliveries.Add(() => watch.Registration.DeliverAsync(response, context.CancellationToken));
            }
        }
    }

    private void RemoveDeltaWatch(string nodeId, long watchId)
    {
        lock (gate)
        {
            if (status.TryGetValue(nodeId, out var info) && info.DeltaWatches.Remove(watchId))
            {
                RemoveIdleStatus(nodeId, info);
            }
        }
    }

    private sealed record PendingDeltaWatch(DeltaDiscoveryRequest Request, ISubscription Subscription, ResponseWatchRegistration<IDeltaResponse> Registration);
}
