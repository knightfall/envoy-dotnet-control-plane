using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Example;
using Envoy.ControlPlane.Resources;

namespace Envoy.ControlPlane.EnvoyProbe;

internal static class ProbeResources
{
    internal static readonly string[] Types = [ResourceTypeUrls.Cluster, ResourceTypeUrls.Endpoint, ResourceTypeUrls.Route, ResourceTypeUrls.Listener];

    internal static Snapshot Create(IReadOnlyDictionary<string, string> versions, int backendPort,
        int listenerPort, string rewrite = "", bool invalidRoute = false,
        bool removeCluster = false, TimeSpan? clusterTtl = null)
    {
        // Every call creates fresh messages. These edits occur before publication.
        var graph = ExampleResources.CreateSnapshot("construction", (uint)backendPort, (uint)listenerPort);
        var routes = (RouteConfiguration)graph.GetResources(ResourceTypeUrls.Route)!["routes"]!;
        routes.VirtualHosts[0].Routes[0].Route_.PrefixRewrite = rewrite;
        if (invalidRoute) routes.VirtualHosts[0].Domains.Clear();
        var groups = Types.ToDictionary(type => type,
            type => new ResourceCollection(versions[type], Entries(type)), StringComparer.Ordinal);
        var snapshot = new Snapshot(groups);
        if (!removeCluster) snapshot.EnsureConsistent();
        return snapshot;

        IEnumerable<ResourceEntry> Entries(string type)
        {
            if (type == ResourceTypeUrls.Cluster && removeCluster) return [];
            var entries = graph.GetResourcesAndTtl(type)!.Values;
            return type == ResourceTypeUrls.Cluster && clusterTtl is not null
                ? entries.Select(entry => new ResourceEntry(entry.Resource, clusterTtl)) : entries;
        }
    }
}
