namespace Envoy.ControlPlane.Streams;

/// <summary>Delivery ordering and source-equivalent legacy wildcard settings for SotW.</summary>
public sealed class SotwOptions
{
    /// <summary>Gets whether ADS uses ordered response delivery and drains on watch replacement.</summary>
    public bool OrderedAds { get; init; }

    /// <summary>Gets whether legacy empty-request wildcard behavior is disabled for every type.</summary>
    public bool DisableLegacyWildcard { get; init; }

    /// <summary>Gets the type URLs whose legacy empty-request wildcard behavior is disabled.</summary>
    public IReadOnlySet<string> LegacyWildcardDisabledTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Returns whether an initial empty request uses legacy wildcard behavior.</summary>
    /// <param name="typeUrl">The stream's resource type URL.</param>
    public bool IsLegacyWildcardActive(string typeUrl) => !DisableLegacyWildcard && !LegacyWildcardDisabledTypes.Contains(typeUrl, StringComparer.Ordinal);
}
