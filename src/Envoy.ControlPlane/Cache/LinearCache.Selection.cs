// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.ControlPlane.Resources;

namespace Envoy.ControlPlane.Cache;

public sealed partial class LinearCache
{
    private Selection? Select(ISubscription subscription, bool delta, bool replyEvenIfEmpty)
    {
        var matched = GetSubscribedResources(subscription);
        var changed = new List<LinearResource>();
        var known = subscription.ReturnedResources;
        foreach (var resource in matched)
        {
            if (!known.TryGetValue(resource.Name, out string? previous) || previous != resource.GetVersion(delta))
            {
                changed.Add(resource);
            }
        }
        var removed = new List<string>();
        foreach (string name in known.Keys)
        {
            if (!resources.ContainsKey(name) || !IsSubscribed(subscription, name))
            {
                removed.Add(name);
            }
        }
        if (!replyEvenIfEmpty && changed.Count == 0 && removed.Count == 0)
        {
            return null;
        }
        bool fullState = !delta && ResourceCatalog.RequiresFullStateInSotw(typeUrl);
        if (!delta && !fullState && !replyEvenIfEmpty && changed.Count == 0)
        {
            return null;
        }
        var selected = fullState ? matched : changed;
        var returned = new Dictionary<string, string>(known, StringComparer.Ordinal);
        foreach (var resource in selected)
        {
            returned[resource.Name] = resource.GetVersion(delta);
        }
        foreach (string name in removed)
        {
            returned.Remove(name);
        }
        return new Selection(selected, removed, returned);
    }

    private List<LinearResource> GetSubscribedResources(ISubscription subscription)
    {
        if (subscription.IsWildcard)
        {
            return resources.Values.ToList();
        }
        var matched = new List<LinearResource>();
        foreach (string name in subscription.SubscribedResources)
        {
            if (resources.TryGetValue(name, out var resource))
            {
                matched.Add(resource);
            }
        }
        if (subscription.SubscribedPrefixes.Count != 0)
        {
            foreach (var (name, resource) in resources)
            {
                if (!subscription.SubscribedResources.Contains(name) && MatchesPrefix(subscription, name))
                {
                    matched.Add(resource);
                }
            }
        }
        return matched;
    }

    private static bool IsSubscribed(ISubscription subscription, string name) =>
        subscription.IsWildcard || subscription.SubscribedResources.Contains(name) || MatchesPrefix(subscription, name);

    private static bool MatchesPrefix(ISubscription subscription, string name) =>
        subscription.SubscribedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private sealed record Selection(List<LinearResource> Resources, List<string> Removed, Dictionary<string, string> Returned);
}
