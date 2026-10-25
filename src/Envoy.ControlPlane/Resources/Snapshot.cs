// Copyright 2018 Envoyproxy Authors. Licensed under Apache-2.0.
using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

/// <summary>A partial snapshot of known resource families with explicit consistency and Delta versions.</summary>
/// <remarks>Retained messages must remain stable. Publish replacement messages and snapshots for updates.</remarks>
public sealed class Snapshot : IResourceSnapshot
{
    private readonly ResourceCollection?[] groups = new ResourceCollection?[(int)ResponseType.Unknown];
    private Dictionary<string, IReadOnlyDictionary<string, string>>? versionMap;

    /// <summary>Creates a partial snapshot retaining messages and optional TTLs.</summary>
    /// <param name="version">The opaque version for supplied groups.</param>
    /// <param name="resources">Known type URLs and retained entries.</param>
    /// <exception cref="ArgumentException">A type URL is unknown.</exception>
    public Snapshot(string version, IReadOnlyDictionary<string, IEnumerable<ResourceEntry>> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var (typeUrl, entries) in resources)
        {
            var type = ResourceCatalog.GetResponseType(typeUrl);
            if (type == ResponseType.Unknown)
            {
                throw new ArgumentException("Unknown resource type: " + typeUrl, nameof(resources));
            }
            groups[(int)type] = new ResourceCollection(version, entries);
        }
    }

    /// <summary>Creates a partial snapshot of messages without TTLs.</summary>
    /// <param name="version">The opaque version for supplied groups.</param>
    /// <param name="resources">Known type URLs and retained messages.</param>
    public Snapshot(string version, IReadOnlyDictionary<string, IEnumerable<IMessage?>> resources)
        : this(version, resources.ToDictionary(pair => pair.Key,
            pair => pair.Value.Select(message => new ResourceEntry(message)), StringComparer.Ordinal))
    {
    }

    /// <summary>Creates a partial snapshot from independently versioned resource groups.</summary>
    /// <param name="resources">Known type URLs and their stable, versioned collections.</param>
    /// <exception cref="ArgumentException">A type URL is unknown.</exception>
    public Snapshot(IReadOnlyDictionary<string, ResourceCollection> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var (typeUrl, collection) in resources)
        {
            var type = ResourceCatalog.GetResponseType(typeUrl);
            if (type == ResponseType.Unknown)
            {
                throw new ArgumentException("Unknown resource type: " + typeUrl, nameof(resources));
            }
            groups[(int)type] = collection;
        }
    }

    /// <inheritdoc/>
    public string GetVersion(string typeUrl) => GetCollection(typeUrl)?.Version ?? "";

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl) => GetCollection(typeUrl)?.Items;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl) => GetResourcesAndTtl(typeUrl)?
        .ToDictionary(pair => pair.Key, pair => pair.Value.Resource, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl) => versionMap?.GetValueOrDefault(typeUrl);

    /// <inheritdoc/>
    public void ConstructVersionMap()
    {
        if (versionMap is not null)
        {
            return;
        }
        versionMap = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        for (int index = 0; index < groups.Length; index++)
        {
            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            versionMap[ResourceCatalog.GetTypeUrl((ResponseType)index)] = versions;
            if (groups[index] is not { } group)
            {
                continue;
            }
            foreach (var entry in group.Items.Values)
            {
                byte[] bytes = ResourceSerialization.Serialize(entry.Resource);
                versions[ResourceCatalog.GetName(entry.Resource)] = ResourceSerialization.ComputeHash(bytes);
            }
        }
    }

    /// <summary>Checks exact EDS/RDS reference sets; construction does not invoke this check.</summary>
    /// <exception cref="InvalidOperationException">An EDS/RDS reference set differs from its resources.</exception>
    public void EnsureConsistent()
    {
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var type in new[] { ResponseType.Cluster, ResponseType.Listener, ResponseType.ScopedRoute, ResponseType.FilterChain })
        {
            if (groups[(int)type] is { } group)
            {
                ResourceReferences.AddReferences(group.Items.Values, references);
            }
        }
        foreach (string typeUrl in new[] { ResourceTypeUrls.Endpoint, ResourceTypeUrls.Route })
        {
            var items = GetResourcesAndTtl(typeUrl);
            var expected = references.GetValueOrDefault(typeUrl);
            bool sameCount = (items?.Count ?? 0) == (expected?.Count ?? 0);
            bool sameNames = expected?.SetEquals(items?.Keys ?? []) ?? true;
            if (!sameCount || !sameNames)
            {
                throw new InvalidOperationException("Inconsistent resource references for " + typeUrl);
            }
        }
    }

    private ResourceCollection? GetCollection(string typeUrl)
    {
        var type = ResourceCatalog.GetResponseType(typeUrl);
        return type == ResponseType.Unknown ? null : groups[(int)type];
    }
}
