// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted from pinned SnapshotCache createDeltaResponse; prefix selection is intentionally absent.
using System.Collections.ObjectModel;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

internal static class DeltaResponseSelection
{
    internal static IDeltaResponse? Create(IResourceSnapshot snapshot, DeltaDiscoveryRequest request,
        ISubscription subscription, bool replyIfEmpty, ResponseContext context, WildcardSelections? shared = null)
    {
        var resources = snapshot.GetResourcesAndTtl(request.TypeUrl);
        var prepared = (snapshot as Snapshot)?.GetDeltaResources(request.TypeUrl);
        var known = subscription.ReturnedResources;
        var resend = subscription.DeltaResourcesToResend;
        if (subscription.IsWildcard && prepared is not null && HasOrdinalNames(known))
        {
            var changes = shared is not null && known is SnapshotResourceVersions previous && resend is null
                ? shared.GetOrSelect(request.TypeUrl, resources, prepared.Versions, previous)
                : SelectWildcard(resources, prepared.Versions, known, resend);
            return changes.IsEmpty && !replyIfEmpty ? null
                : new RawDeltaResponse(request, snapshot.GetVersion(request.TypeUrl), changes.Added, changes.Removed,
                    prepared.Versions, context, prepared);
        }

        var versions = prepared?.Versions ?? snapshot.GetVersionMap(request.TypeUrl);
        var returned = new Dictionary<string, string>(known, StringComparer.Ordinal);
        var changed = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
        if (subscription.IsWildcard && resources is not null)
        {
            foreach (var (name, entry) in resources)
            {
                AddIfChanged(name, entry);
            }
        }
        foreach (string name in subscription.SubscribedResources)
        {
            if (resources?.TryGetValue(name, out var entry) == true)
            {
                AddIfChanged(name, entry);
            }
        }
        var removed = new List<string>();
        foreach (string name in returned.Keys)
        {
            bool exists = resources?.ContainsKey(name) == true;
            if (!exists || (!subscription.IsWildcard && !subscription.SubscribedResources.Contains(name)))
            {
                removed.Add(name);
            }
        }
        if (changed.Count == 0 && removed.Count == 0 && !replyIfEmpty)
        {
            return null;
        }
        foreach (string name in removed)
        {
            returned.Remove(name);
        }
        return new RawDeltaResponse(request, snapshot.GetVersion(request.TypeUrl), changed, removed,
            new ReadOnlyDictionary<string, string>(returned), context, prepared);

        void AddIfChanged(string name, ResourceEntry entry)
        {
            string version = versions?.GetValueOrDefault(name) ?? "";
            if (returned.TryGetValue(name, out string? previous) && previous == version && resend?.Contains(name) != true)
            {
                return;
            }
            changed[name] = entry;
            returned[name] = version;
        }
    }

    private static bool HasOrdinalNames(IReadOnlyDictionary<string, string> known) =>
        known is SnapshotResourceVersions || (known is Dictionary<string, string> dictionary
            && dictionary.GetType() == typeof(Dictionary<string, string>)
            && (dictionary.Comparer == StringComparer.Ordinal || dictionary.Comparer == EqualityComparer<string>.Default));

    private static Changes SelectWildcard(IReadOnlyDictionary<string, ResourceEntry>? resources, SnapshotResourceVersions versions,
        IReadOnlyDictionary<string, string> known, IReadOnlySet<string>? resend)
    {
        if (ReferenceEquals(versions, known) && resend is null)
        {
            return Changes.Empty;
        }
        Dictionary<string, ResourceEntry>? changed = null;
        int retainedCount = 0;
        foreach (var (name, version) in versions)
        {
            if (known.TryGetValue(name, out string? previous))
            {
                retainedCount++;
                if (previous == version && resend?.Contains(name) != true)
                {
                    continue;
                }
            }
            (changed ??= new Dictionary<string, ResourceEntry>(StringComparer.Ordinal)).Add(name, resources![name]);
        }
        List<string>? removed = null;
        if (retainedCount != known.Count)
        {
            foreach (var (name, _) in known)
            {
                if (!versions.ContainsKey(name))
                {
                    (removed ??= []).Add(name);
                }
            }
        }
        return new Changes(changed ?? Changes.Empty.Added, removed ?? Changes.Empty.Removed);
    }

    internal sealed class WildcardSelections
    {
        private readonly Dictionary<(string TypeUrl, SnapshotResourceVersions Previous), Changes> bySubscription = new();

        internal Changes GetOrSelect(string typeUrl, IReadOnlyDictionary<string, ResourceEntry>? resources,
            SnapshotResourceVersions versions, SnapshotResourceVersions previous)
        {
            var key = (typeUrl, previous);
            if (!bySubscription.TryGetValue(key, out var changes))
            {
                changes = SelectWildcard(resources, versions, previous, null);
                bySubscription.Add(key, changes);
            }
            return changes;
        }
    }

    internal readonly record struct Changes(IReadOnlyDictionary<string, ResourceEntry> Added, IReadOnlyList<string> Removed)
    {
        internal static readonly Changes Empty = new(new Dictionary<string, ResourceEntry>(StringComparer.Ordinal), []);
        internal bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
    }
}
