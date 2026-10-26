// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted for .NET collection ownership from the pinned stream subscription behavior.
using Envoy.ControlPlane.Cache;

namespace Envoy.ControlPlane.Streams;

/// <summary>Stream-owned Delta subscription state; cancel the current watch before changing it.</summary>
public sealed class DeltaSubscription : ISubscription
{
    private bool allowLegacyWildcard;
    private readonly HashSet<string> subscribedResources = new(StringComparer.Ordinal);
    private readonly HashSet<string> subscribedPrefixes = new(StringComparer.Ordinal);
    private Dictionary<string, string> returnedResources;

    /// <summary>Creates one type's initial state, including its first-request resource versions.</summary>
    /// <param name="subscribed">Initial exact, prefix or wildcard additions.</param>
    /// <param name="unsubscribed">Initial removals, applied after additions.</param>
    /// <param name="initialResourceVersions">Versions known on this new stream; copied into owned bookkeeping.</param>
    /// <param name="allowLegacyWildcard">Whether initial empty additions imply wildcard.</param>
    public DeltaSubscription(IReadOnlyCollection<string>? subscribed = null, IReadOnlyCollection<string>? unsubscribed = null,
        IReadOnlyDictionary<string, string>? initialResourceVersions = null, bool allowLegacyWildcard = true)
    {
        this.allowLegacyWildcard = allowLegacyWildcard;
        IsWildcard = allowLegacyWildcard && (subscribed is null || subscribed.Count == 0);
        returnedResources = initialResourceVersions is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(initialResourceVersions, StringComparer.Ordinal);
        UpdateResourceSubscriptions(subscribed, unsubscribed);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ReturnedResources => returnedResources;

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedResources => subscribedResources;

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedPrefixes => subscribedPrefixes;

    /// <inheritdoc/>
    public bool IsWildcard { get; private set; }

    /// <summary>Applies additions before removals without re-seeding initial versions.</summary>
    /// <param name="subscribed">New exact, prefix or wildcard additions.</param>
    /// <param name="unsubscribed">Exact, prefix or wildcard removals.</param>
    public void UpdateResourceSubscriptions(IReadOnlyCollection<string>? subscribed, IReadOnlyCollection<string>? unsubscribed)
    {
        if (allowLegacyWildcard)
        {
            if (subscribed is null || subscribed.Count == 0)
            {
                return;
            }
            allowLegacyWildcard = false;
        }

        foreach (string name in subscribed ?? [])
        {
            if (name == "*")
            {
                IsWildcard = true;
            }
            else if (name.EndsWith("/*", StringComparison.Ordinal))
            {
                subscribedPrefixes.Add(name[..^1]);
            }
            else
            {
                subscribedResources.Add(name);
            }
        }
        foreach (string name in unsubscribed ?? [])
        {
            if (name == "*")
            {
                IsWildcard = false;
            }
            else if (name.EndsWith("/*", StringComparison.Ordinal))
            {
                subscribedPrefixes.Remove(name[..^1]);
            }
            else
            {
                if (IsWildcard && subscribedResources.Contains(name))
                {
                    // Force the cache to report an existing resource again or confirm its removal.
                    returnedResources[name] = "";
                }
                else
                {
                    returnedResources.Remove(name);
                }
                subscribedResources.Remove(name);
            }
        }
    }

    /// <summary>Replaces returned version metadata using a copy owned by this stream state.</summary>
    /// <param name="resourceVersions">Resource versions from the delivered response.</param>
    public void SetReturnedResources(IReadOnlyDictionary<string, string> resourceVersions) =>
        returnedResources = new Dictionary<string, string>(resourceVersions, StringComparer.Ordinal);
}
