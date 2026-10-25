using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

internal static class SotwResponseSelection
{
    internal static (ISotwResponse? Response, bool Untracked) Create(IResourceSnapshot snapshot, DiscoveryRequest request,
        ISubscription subscription, bool ads, ResponseContext context)
    {
        var resources = snapshot.GetResourcesAndTtl(request.TypeUrl) ?? new Dictionary<string, ResourceEntry>();
        var subscribed = subscription.SubscribedResources;
        var known = subscription.ReturnedResources;
        if (ads && !subscription.IsWildcard && resources.Keys.Any(name => !subscribed.Contains(name)))
        {
            return (null, true);
        }

        string version = snapshot.GetVersion(request.TypeUrl);
        bool fullState = ResourceCatalog.RequiresFullStateInSotw(request.TypeUrl);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var deleted = new List<string>();
        if (version == request.VersionInfo)
        {
            if (subscription.IsWildcard)
            {
                foreach (string name in resources.Keys)
                {
                    if (!known.ContainsKey(name))
                    {
                        changed.Add(name);
                    }
                }
            }
            foreach (string name in subscribed)
            {
                if (resources.ContainsKey(name) && !known.ContainsKey(name))
                {
                    changed.Add(name);
                }
            }
            if (changed.Count == 0 && !fullState)
            {
                return (null, false);
            }
            foreach (string name in known.Keys)
            {
                if (!resources.ContainsKey(name) || (!subscription.IsWildcard && !subscribed.Contains(name)))
                {
                    deleted.Add(name);
                }
            }
            if (changed.Count == 0 && deleted.Count == 0)
            {
                return (null, false);
            }
        }

        var selected = new List<ResourceEntry>();
        Dictionary<string, string> returned;
        if (version != request.VersionInfo || fullState)
        {
            returned = new Dictionary<string, string>(StringComparer.Ordinal);
            if (subscription.IsWildcard)
            {
                foreach (var (name, entry) in resources)
                {
                    selected.Add(entry);
                    returned[name] = version;
                }
            }
            foreach (string name in subscribed)
            {
                if (resources.TryGetValue(name, out var entry))
                {
                    selected.Add(entry);
                    returned[name] = version;
                }
            }
        }
        else
        {
            returned = new Dictionary<string, string>(known, StringComparer.Ordinal);
            foreach (string name in changed)
            {
                selected.Add(resources[name]);
                returned[name] = version;
            }
            foreach (string name in deleted)
            {
                returned.Remove(name);
            }
        }
        return (new RawSotwResponse(request, version, selected, returned, context), false);
    }
}
