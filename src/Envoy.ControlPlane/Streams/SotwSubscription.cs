using Envoy.ControlPlane.Cache;

namespace Envoy.ControlPlane.Streams;

/// <summary>Stream-owned SotW subscription state; cancel the current watch before changing it.</summary>
public sealed class SotwSubscription : ISubscription
{
    private bool allowLegacyWildcard;
    private HashSet<string> subscribedResources = new(StringComparer.Ordinal);
    private HashSet<string> subscribedPrefixes = new(StringComparer.Ordinal);
    private Dictionary<string, string> returnedResources = new(StringComparer.Ordinal);

    /// <summary>Creates the initial subscription, including legacy empty-as-wildcard behavior.</summary>
    /// <param name="subscribed">The initial full resource-name list; null is an empty request.</param>
    /// <param name="allowLegacyWildcard">Whether initial empty requests subscribe to all resources.</param>
    public SotwSubscription(IEnumerable<string>? subscribed = null, bool allowLegacyWildcard = true)
    {
        this.allowLegacyWildcard = allowLegacyWildcard;
        IsWildcard = allowLegacyWildcard;
        SetResourceSubscription(subscribed);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ReturnedResources => returnedResources;

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedResources => subscribedResources;

    /// <inheritdoc/>
    public IReadOnlySet<string> SubscribedPrefixes => subscribedPrefixes;

    /// <inheritdoc/>
    public bool IsWildcard { get; private set; }

    /// <summary>Replaces the full subscription and removes returned metadata no longer covered.</summary>
    /// <param name="subscribed">The next full name list; null is an empty request.</param>
    public void SetResourceSubscription(IEnumerable<string>? subscribed)
    {
        string[] names = subscribed?.ToArray() ?? [];
        if (allowLegacyWildcard)
        {
            if (names.Length == 0)
            {
                return;
            }
            allowLegacyWildcard = false;
        }

        var exact = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        bool wildcard = false;
        foreach (string name in names)
        {
            if (name == "*")
            {
                wildcard = true;
            }
            else if (name.EndsWith("/*", StringComparison.Ordinal))
            {
                prefixes.Add(name[..^1]);
            }
            else
            {
                exact.Add(name);
            }
        }
        if (!wildcard)
        {
            foreach (string name in returnedResources.Keys.ToArray())
            {
                if (!exact.Contains(name) && !MatchesPrefix(prefixes, name))
                {
                    returnedResources.Remove(name);
                }
            }
        }
        IsWildcard = wildcard;
        subscribedResources = exact;
        subscribedPrefixes = prefixes;
    }

    /// <summary>Copies returned metadata into the stream's mutable bookkeeping.</summary>
    /// <param name="resourceVersions">Resource versions from the delivered response.</param>
    public void SetReturnedResources(IReadOnlyDictionary<string, string> resourceVersions)
    {
        returnedResources = new Dictionary<string, string>(resourceVersions, StringComparer.Ordinal);
    }

    /// <summary>Checks that all returned names still match the current subscription.</summary>
    /// <param name="response">The possibly superseded SotW response.</param>
    public bool MatchesResponse(ISotwResponse response)
    {
        if (IsWildcard)
        {
            return true;
        }
        foreach (string name in response.ReturnedResources.Keys)
        {
            if (!subscribedResources.Contains(name) && !MatchesPrefix(subscribedPrefixes, name))
            {
                return false;
            }
        }
        return true;
    }

    private static bool MatchesPrefix(IEnumerable<string> prefixes, string name) =>
        prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
