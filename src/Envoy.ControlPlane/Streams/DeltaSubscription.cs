// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted for .NET collection ownership from the pinned stream subscription behavior.
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;

namespace Envoy.ControlPlane.Streams;

/// <summary>Stream-owned Delta subscription state; cancel the current watch before changing it.</summary>
public sealed class DeltaSubscription : ISubscription
{
    private bool allowLegacyWildcard;
    private readonly HashSet<string> subscribedResources = new(StringComparer.Ordinal);
    private readonly HashSet<string> subscribedPrefixes = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> returnedResources;
    private HashSet<string>? resourcesToResend;
    private HashSet<string>? pendingWildcardResets;
    private long subscriptionCharacters;
    private long wildcardResetCharacters;

    internal long RetainedEntryCount => (long)subscribedResources.Count + subscribedPrefixes.Count + (pendingWildcardResets?.Count ?? 0);
    internal long RetainedCharacters => subscriptionCharacters + wildcardResetCharacters;

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
        ApplyResourceSubscriptions(subscribed, unsubscribed, resendKnownResources: false);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ReturnedResources => returnedResources;

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedResources => subscribedResources;

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedPrefixes => subscribedPrefixes;

    /// <inheritdoc/>
    public bool IsWildcard { get; private set; }

    IReadOnlySet<string>? ISubscription.DeltaResourcesToResend => resourcesToResend is { Count: > 0 } ? resourcesToResend : null;

    /// <summary>Applies additions before removals without re-seeding initial versions.</summary>
    /// <param name="subscribed">New exact, prefix or wildcard additions.</param>
    /// <param name="unsubscribed">Exact, prefix or wildcard removals.</param>
    public void UpdateResourceSubscriptions(IReadOnlyCollection<string>? subscribed, IReadOnlyCollection<string>? unsubscribed) =>
        ApplyResourceSubscriptions(subscribed, unsubscribed, resendKnownResources: true);

    private void ApplyResourceSubscriptions(IReadOnlyCollection<string>? subscribed, IReadOnlyCollection<string>? unsubscribed, bool resendKnownResources)
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
                if (subscribedPrefixes.Add(name[..^1]))
                {
                    subscriptionCharacters += name.Length - 1;
                }
            }
            else
            {
                if (subscribedResources.Add(name))
                {
                    subscriptionCharacters += name.Length;
                }
                if (resendKnownResources && returnedResources.ContainsKey(name))
                {
                    (resourcesToResend ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
                }
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
                if (subscribedPrefixes.Remove(name[..^1]))
                {
                    subscriptionCharacters -= name.Length - 1;
                }
            }
            else
            {
                resourcesToResend?.Remove(name);
                if (IsWildcard && subscribedResources.Contains(name))
                {
                    // Force the cache to report an existing resource again or confirm its removal.
                    if (!returnedResources.ContainsKey(name) &&
                        (pendingWildcardResets ??= new HashSet<string>(StringComparer.Ordinal)).Add(name))
                    {
                        wildcardResetCharacters += name.Length;
                    }
                    GetWritableReturnedResources()[name] = "";
                }
                else if (returnedResources.ContainsKey(name))
                {
                    GetWritableReturnedResources().Remove(name);
                    if (pendingWildcardResets?.Remove(name) == true)
                    {
                        wildcardResetCharacters -= name.Length;
                    }
                }
                if (subscribedResources.Remove(name))
                {
                    subscriptionCharacters -= name.Length;
                }
            }
        }
    }

    /// <summary>Replaces returned version metadata using a copy owned by this stream state.</summary>
    /// <param name="resourceVersions">Resource versions from the delivered response.</param>
    public void SetReturnedResources(IReadOnlyDictionary<string, string> resourceVersions)
    {
        returnedResources = new Dictionary<string, string>(resourceVersions, StringComparer.Ordinal);
        ClearPendingChanges();
    }

    internal void SetReturnedResourcesFromResponse(IReadOnlyDictionary<string, string> resourceVersions)
    {
        if (resourceVersions is SnapshotResourceVersions versions)
        {
            returnedResources = versions;
            ClearPendingChanges();
        }
        else
        {
            SetReturnedResources(resourceVersions);
        }
    }

    private void ClearPendingChanges()
    {
        resourcesToResend?.Clear();
        pendingWildcardResets?.Clear();
        wildcardResetCharacters = 0;
    }

    private Dictionary<string, string> GetWritableReturnedResources()
    {
        if (returnedResources is not Dictionary<string, string> writable)
        {
            writable = new Dictionary<string, string>(returnedResources, StringComparer.Ordinal);
            returnedResources = writable;
        }
        return writable;
    }
}
