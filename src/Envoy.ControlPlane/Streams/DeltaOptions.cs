namespace Envoy.ControlPlane.Streams;

/// <summary>Subscription limits and legacy wildcard settings for Delta streams.</summary>
public sealed class DeltaOptions
{
    /// <summary>Gets the maximum number of distinct resource types accepted during the life of an ADS stream.</summary>
    public int MaxAdsResourceTypes { get; init; } = 64;

    /// <summary>Gets the maximum number of retained explicit names, prefixes, pending wildcard resets, and client version map entries across one Delta stream.</summary>
    public int MaxSubscriptionEntries { get; init; } = 16_384;

    /// <summary>Gets the maximum combined length, in UTF-16 code units, of retained explicit names, prefixes, pending wildcard resets, and client version map keys and values across one Delta stream.</summary>
    public int MaxSubscriptionCharacters { get; init; } = 1_048_576;

    /// <summary>Gets whether initial empty additions stop implying wildcard for every type.</summary>
    public bool DisableLegacyWildcard { get; init; }

    /// <summary>Gets the type URLs whose initial empty additions do not imply wildcard.</summary>
    public IReadOnlySet<string> LegacyWildcardDisabledTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Returns whether initial empty additions imply wildcard for this type.</summary>
    /// <param name="typeUrl">The request's resource type URL.</param>
    public bool IsLegacyWildcardActive(string typeUrl) => !DisableLegacyWildcard && !LegacyWildcardDisabledTypes.Contains(typeUrl, StringComparer.Ordinal);
}
