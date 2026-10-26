// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

public sealed partial class SnapshotCache
{
    /// <summary>Fetches all resources or literal requested names without creating watch status.</summary>
    /// <param name="request">The originating request, retained by the lazy response.</param>
    /// <param name="context">The caller context forwarded to the response.</param>
    /// <exception cref="KeyNotFoundException">No snapshot exists for the hashed node.</exception>
    /// <exception cref="SkipFetchException">The requested version already matches the snapshot.</exception>
    public Task<ISotwResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string nodeId = nodeHasher.Hash(request.Node);
        lock (gate)
        {
            if (!snapshots.TryGetValue(nodeId, out var snapshot))
            {
                throw new KeyNotFoundException("Missing snapshot for node " + nodeId);
            }
            string version = snapshot.GetVersion(request.TypeUrl);
            if (version == request.VersionInfo)
            {
                throw new SkipFetchException();
            }
            var selected = new List<ResourceEntry>();
            if (snapshot.GetResourcesAndTtl(request.TypeUrl) is { } resources)
            {
                if (request.ResourceNames.Count == 0)
                {
                    selected.AddRange(resources.Values);
                }
                else
                {
                    foreach (string name in request.ResourceNames)
                    {
                        if (resources.TryGetValue(name, out var entry))
                        {
                            selected.Add(entry);
                        }
                    }
                }
            }
            return Task.FromResult<ISotwResponse>(new RawSotwResponse(request, version, selected,
                new Dictionary<string, string>(), context));
        }
    }
}
