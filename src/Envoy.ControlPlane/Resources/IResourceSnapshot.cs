using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

/// <summary>A stable resource snapshot, including application-defined resource families.</summary>
public interface IResourceSnapshot
{
    /// <summary>Returns an opaque type version; compare versions only for equality.</summary>
    /// <param name="typeUrl">The requested resource type URL.</param>
    string GetVersion(string typeUrl);

    /// <summary>Returns resources and TTLs, or null when the type is absent.</summary>
    /// <param name="typeUrl">The requested resource type URL.</param>
    IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl);

    /// <summary>Returns resources without TTLs, or null when the type is absent.</summary>
    /// <param name="typeUrl">The requested resource type URL.</param>
    IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl);

    /// <summary>Constructs per-resource versions for subsequent Delta reads.</summary>
    void ConstructVersionMap();

    /// <summary>Returns constructed per-resource versions, or null when unavailable.</summary>
    /// <param name="typeUrl">The requested resource type URL.</param>
    IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl);
}
