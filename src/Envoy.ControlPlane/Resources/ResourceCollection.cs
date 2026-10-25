using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

/// <summary>A versioned name index retaining the original messages and TTLs.</summary>
public sealed class ResourceCollection
{
    /// <summary>Creates a resource index; the last duplicate name wins.</summary>
    /// <param name="version">The opaque collection version.</param>
    /// <param name="resources">The retained messages and TTLs.</param>
    public ResourceCollection(string version, IEnumerable<ResourceEntry> resources)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(resources);
        Version = version;
        var items = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
        foreach (var entry in resources)
        {
            items[ResourceCatalog.GetName(entry.Resource)] = entry;
        }
        Items = items;
    }

    /// <summary>Creates an index of resources without TTLs.</summary>
    /// <param name="version">The opaque collection version.</param>
    /// <param name="resources">The retained messages.</param>
    public ResourceCollection(string version, IEnumerable<IMessage?> resources)
        : this(version, resources.Select(resource => new ResourceEntry(resource)))
    {
    }

    /// <summary>Gets the caller-supplied opaque version.</summary>
    public string Version { get; }

    /// <summary>Gets the indexed entries. Messages must remain stable while retained.</summary>
    public IReadOnlyDictionary<string, ResourceEntry> Items { get; }
}
