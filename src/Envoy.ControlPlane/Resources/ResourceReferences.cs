// Copyright 2018 Envoyproxy Authors. Licensed under Apache-2.0.
using Envoy.Config.Cluster.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using ListenerFilter = Envoy.Config.Listener.V3.Filter;

namespace Envoy.ControlPlane.Resources;

/// <summary>Extracts the upstream EDS/RDS references used by snapshot consistency checks.</summary>
public static class ResourceReferences
{
    /// <summary>Collects dependent resource names by type URL.</summary>
    /// <param name="resources">The messages whose references are inspected.</param>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GetReferences(IEnumerable<ResourceEntry> resources)
    {
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        AddReferences(resources, references);
        return references.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Unpacks a valid HTTP connection manager, returning null for unrelated or malformed configuration.</summary>
    /// <param name="filter">The listener filter carrying typed configuration.</param>
    public static HttpConnectionManager? GetHttpConnectionManager(ListenerFilter? filter)
    {
        try
        {
            return filter?.TypedConfig?.TryUnpack<HttpConnectionManager>(out var manager) == true ? manager : null;
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    internal static void AddReferences(IEnumerable<ResourceEntry> resources, Dictionary<string, HashSet<string>> references)
    {
        foreach (var entry in resources)
        {
            switch (entry.Resource)
            {
                case Cluster cluster when cluster.ClusterDiscoveryTypeCase == Cluster.ClusterDiscoveryTypeOneofCase.Type
                    && cluster.Type == Cluster.Types.DiscoveryType.Eds:
                    string? endpoint = cluster.EdsClusterConfig?.ServiceName;
                    Add(references, ResourceTypeUrls.Endpoint, string.IsNullOrEmpty(endpoint) ? cluster.Name : endpoint);
                    break;
                case Listener listener:
                    foreach (var chain in listener.FilterChains)
                    {
                        AddChainReferences(chain, references);
                    }
                    if (listener.DefaultFilterChain is { } defaultChain)
                    {
                        AddChainReferences(defaultChain, references);
                    }
                    break;
                case FilterChain chain:
                    AddChainReferences(chain, references);
                    break;
                case ScopedRouteConfiguration scoped:
                    Add(references, ResourceTypeUrls.Route, scoped.RouteConfigurationName);
                    break;
            }
        }
    }

    private static void AddChainReferences(FilterChain chain, Dictionary<string, HashSet<string>> references)
    {
        foreach (var filter in chain.Filters)
        {
            var manager = GetHttpConnectionManager(filter);
            if (manager?.Rds?.RouteConfigName is { Length: > 0 } route)
            {
                Add(references, ResourceTypeUrls.Route, route);
            }
            if (manager?.ScopedRoutes?.ScopedRouteConfigurationsList is { } scopedList)
            {
                foreach (var scoped in scopedList.ScopedRouteConfigurations)
                {
                    Add(references, ResourceTypeUrls.Route, scoped.RouteConfigurationName);
                }
            }
        }
    }

    private static void Add(Dictionary<string, HashSet<string>> references, string typeUrl, string name)
    {
        if (!references.TryGetValue(typeUrl, out var names))
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            references[typeUrl] = names;
        }
        names.Add(name);
    }
}
