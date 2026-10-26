// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted from pinned SnapshotCache createDeltaResponse; prefix selection is intentionally absent.
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

internal static class DeltaResponseSelection
{
    internal static IDeltaResponse? Create(IResourceSnapshot snapshot, DeltaDiscoveryRequest request,
        ISubscription subscription, bool replyIfEmpty, ResponseContext context)
    {
        var resources = snapshot.GetResourcesAndTtl(request.TypeUrl);
        var versions = snapshot.GetVersionMap(request.TypeUrl);
        var returned = new Dictionary<string, string>(subscription.ReturnedResources, StringComparer.Ordinal);
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
        foreach (string name in returned.Keys.ToArray())
        {
            bool exists = resources?.ContainsKey(name) == true;
            if (!exists || (!subscription.IsWildcard && !subscription.SubscribedResources.Contains(name)))
            {
                removed.Add(name);
                returned.Remove(name);
            }
        }
        if (changed.Count == 0 && removed.Count == 0 && !replyIfEmpty)
        {
            return null;
        }
        return new RawDeltaResponse(request, snapshot.GetVersion(request.TypeUrl), changed, removed, returned, context);

        void AddIfChanged(string name, ResourceEntry entry)
        {
            string version = versions?.GetValueOrDefault(name) ?? "";
            if (returned.TryGetValue(name, out string? known) && known == version)
            {
                return;
            }
            changed[name] = entry;
            returned[name] = version;
        }
    }
}
