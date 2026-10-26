namespace Envoy.ControlPlane.Streams;

/// <summary>Source-equivalent legacy wildcard settings for Delta streams.</summary>
public sealed class DeltaOptions
{
    /// <summary>Gets whether initial empty additions stop implying wildcard for every type.</summary>
    public bool DisableLegacyWildcard { get; init; }

    /// <summary>Gets the type URLs whose initial empty additions do not imply wildcard.</summary>
    public IReadOnlySet<string> LegacyWildcardDisabledTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Returns whether initial empty additions imply wildcard for this type.</summary>
    /// <param name="typeUrl">The request's resource type URL.</param>
    public bool IsLegacyWildcardActive(string typeUrl) => !DisableLegacyWildcard && !LegacyWildcardDisabledTypes.Contains(typeUrl, StringComparer.Ordinal);
}
