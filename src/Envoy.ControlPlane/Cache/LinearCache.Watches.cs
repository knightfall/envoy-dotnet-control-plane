// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using System.Threading.Channels;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

public sealed partial class LinearCache
{
    private readonly Dictionary<long, PendingWatch> watches = new();
    private readonly Dictionary<string, HashSet<long>> exactWatches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<long>> prefixWatches = new(StringComparer.Ordinal);
    private readonly HashSet<long> wildcardWatches = new();
    private long nextWatchId;

    /// <inheritdoc/>
    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(responses);
        ValidateType(request.TypeUrl);
        lock (gate)
        {
            var selection = Select(subscription, delta: false,
                replyEvenIfEmpty: request.VersionInfo.Length == 0 || !request.VersionInfo.StartsWith(versionPrefix, StringComparison.Ordinal));
            long watchId = ++nextWatchId;
            var registration = new ResponseWatchRegistration<ISotwResponse>(gate, responses, () => RemoveWatch(watchId));
            if (selection is not null)
            {
                _ = registration.DeliverAsync(new LinearSotwResponse(request, CurrentVersion, selection.Resources, selection.Returned), default);
            }
            else
            {
                TrackWatch(watchId, new PendingWatch(subscription, false, (selected, currentVersion, cancellation) =>
                    registration.DeliverAsync(new LinearSotwResponse(request, currentVersion, selected.Resources, selected.Returned), cancellation)));
            }
            return registration;
        }
    }

    /// <inheritdoc/>
    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(responses);
        ValidateType(request.TypeUrl);
        lock (gate)
        {
            var selection = Select(subscription, delta: true, replyEvenIfEmpty: subscription.IsWildcard && request.ResponseNonce.Length == 0);
            long watchId = ++nextWatchId;
            var registration = new ResponseWatchRegistration<IDeltaResponse>(gate, responses, () => RemoveWatch(watchId));
            if (selection is not null)
            {
                _ = registration.DeliverAsync(new LinearDeltaResponse(request, CurrentVersion, selection.Resources, selection.Removed, selection.Returned), default);
            }
            else
            {
                TrackWatch(watchId, new PendingWatch(subscription, true, (selected, currentVersion, cancellation) =>
                    registration.DeliverAsync(new LinearDeltaResponse(request, currentVersion, selected.Resources, selected.Removed, selected.Returned), cancellation)));
            }
            return registration;
        }
    }

    /// <summary>Counts exact, prefix and wildcard index entries matching a name; overlaps count separately.</summary>
    /// <param name="name">The name whose watch indexes are counted.</param>
    public int NumWatches(string name)
    {
        lock (gate)
        {
            int count = wildcardWatches.Count + (exactWatches.GetValueOrDefault(name)?.Count ?? 0);
            foreach (var (prefix, indexed) in prefixWatches)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count += indexed.Count;
                }
            }
            return count;
        }
    }

    /// <summary>Gets the number of prefix index entries, including overlaps.</summary>
    public int NumPrefixWatches
    {
        get
        {
            lock (gate)
            {
                return prefixWatches.Values.Sum(indexed => indexed.Count);
            }
        }
    }

    /// <summary>Gets the number of wildcard registrations.</summary>
    public int NumWildcardWatches
    {
        get
        {
            lock (gate)
            {
                return wildcardWatches.Count;
            }
        }
    }

    /// <summary>Gets the number of distinct pending registrations across both response modes.</summary>
    public int NumCacheWatches
    {
        get
        {
            lock (gate)
            {
                return watches.Count;
            }
        }
    }

    private void ValidateType(string requestedType)
    {
        if (requestedType != typeUrl)
        {
            throw new ArgumentException($"Request type {requestedType} does not match cache type {typeUrl}.", nameof(requestedType));
        }
    }

    private void StartDeliveries(HashSet<string> modified, CancellationToken cancellationToken, List<Task<bool>> deliveries)
    {
        var triggered = new HashSet<long>(wildcardWatches);
        foreach (string name in modified)
        {
            if (exactWatches.TryGetValue(name, out var indexed))
            {
                triggered.UnionWith(indexed);
            }
            foreach (var (prefix, matching) in prefixWatches)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    triggered.UnionWith(matching);
                }
            }
        }
        foreach (long watchId in triggered)
        {
            var watch = watches[watchId];
            var selection = Select(watch.Subscription, watch.Delta, replyEvenIfEmpty: false);
            if (selection is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                deliveries.Add(watch.Deliver(selection, CurrentVersion, cancellationToken));
            }
        }
    }

    private void TrackWatch(long watchId, PendingWatch watch)
    {
        watches.Add(watchId, watch);
        if (watch.Subscription.IsWildcard)
        {
            wildcardWatches.Add(watchId);
            return;
        }
        AddToIndex(exactWatches, watch.Subscription.SubscribedResources, watchId);
        AddToIndex(prefixWatches, watch.Subscription.SubscribedPrefixes, watchId);
    }

    private void RemoveWatch(long watchId)
    {
        if (!watches.Remove(watchId, out var watch))
        {
            return;
        }
        wildcardWatches.Remove(watchId);
        RemoveFromIndex(exactWatches, watch.Subscription.SubscribedResources, watchId);
        RemoveFromIndex(prefixWatches, watch.Subscription.SubscribedPrefixes, watchId);
    }

    private static void AddToIndex(Dictionary<string, HashSet<long>> index, IEnumerable<string> keys, long watchId)
    {
        foreach (string key in keys)
        {
            if (!index.TryGetValue(key, out var indexed))
            {
                indexed = new HashSet<long>();
                index.Add(key, indexed);
            }
            indexed.Add(watchId);
        }
    }

    private static void RemoveFromIndex(Dictionary<string, HashSet<long>> index, IEnumerable<string> keys, long watchId)
    {
        foreach (string key in keys)
        {
            if (index.TryGetValue(key, out var indexed))
            {
                indexed.Remove(watchId);
                if (indexed.Count == 0)
                {
                    index.Remove(key);
                }
            }
        }
    }

    private sealed record PendingWatch(ISubscription Subscription, bool Delta, Func<Selection, string, CancellationToken, Task<bool>> Deliver);
}
