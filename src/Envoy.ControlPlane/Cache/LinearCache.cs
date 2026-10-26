// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted from the pinned LinearCache behavior using .NET channel ownership.
using System.Globalization;
using System.Runtime.ExceptionServices;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;

namespace Envoy.ControlPlane.Cache;

/// <summary>Stores one node-independent collection of named, stable messages for SotW and Delta watches.</summary>
/// <remarks>Keep mutation input collections stable until their asynchronous publication completes; retained messages remain stable until replaced.</remarks>
public sealed partial class LinearCache : IConfigWatcher, IConfigFetcher
{
    private readonly object gate = new();
    private readonly SemaphoreSlim publication = new(1, 1);
    private readonly Dictionary<string, LinearResource> resources = new(StringComparer.Ordinal);
    private readonly string typeUrl;
    private readonly string versionPrefix;
    private ulong version;

    /// <summary>Creates a collection at version prefix + 0, copying its index and retaining its messages.</summary>
    /// <param name="typeUrl">The single opaque resource type accepted by watches.</param>
    /// <param name="initialResources">Initial caller-named messages; retained messages must remain stable.</param>
    /// <param name="versionPrefix">An instance identifier prepended to each collection update counter.</param>
    public LinearCache(string typeUrl, IReadOnlyDictionary<string, IMessage?>? initialResources = null, string versionPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(typeUrl);
        ArgumentNullException.ThrowIfNull(versionPrefix);
        this.typeUrl = typeUrl;
        this.versionPrefix = versionPrefix;
        foreach (var (name, resource) in initialResources ?? new Dictionary<string, IMessage?>())
        {
            resources.Add(name, new LinearResource(name, resource, CurrentVersion));
        }
    }

    /// <summary>Updates one named message and advances the collection counter, even for an identical payload.</summary>
    /// <param name="name">The caller's resource name, independent of fields in the message.</param>
    /// <param name="resource">The stable message retained by the cache.</param>
    /// <param name="cancellationToken">Cancels queued publication or channel delivery; an installed update remains installed.</param>
    public Task UpdateResourceAsync(string name, IMessage resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return PublishAsync(modified =>
        {
            resources[name] = new LinearResource(name, resource, CurrentVersion);
            modified.Add(name);
        }, cancellationToken);
    }

    /// <summary>Deletes one named resource and advances the counter even when that name is absent.</summary>
    /// <param name="name">The resource to remove.</param>
    /// <param name="cancellationToken">Cancellation for publication and delivery.</param>
    public Task DeleteResourceAsync(string name, CancellationToken cancellationToken = default) => PublishAsync(modified =>
    {
        resources.Remove(name);
        modified.Add(name);
    }, cancellationToken);

    /// <summary>Applies a batch at one new counter value, with deletions taking precedence over updates.</summary>
    /// <param name="toUpdate">Named stable messages; null values follow upstream bulk-update semantics.</param>
    /// <param name="toDelete">Names deleted after updates.</param>
    /// <param name="cancellationToken">Cancellation for publication and delivery.</param>
    public Task UpdateResourcesAsync(IReadOnlyDictionary<string, IMessage?> toUpdate, IReadOnlyCollection<string> toDelete,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toUpdate);
        ArgumentNullException.ThrowIfNull(toDelete);
        return PublishAsync(modified =>
        {
            foreach (var (name, resource) in toUpdate)
            {
                resources[name] = new LinearResource(name, resource, CurrentVersion);
                modified.Add(name);
            }
            foreach (string name in toDelete)
            {
                resources.Remove(name);
                modified.Add(name);
            }
        }, cancellationToken);
    }

    /// <summary>Replaces the collection; every supplied entry receives the new counter and fresh lazy serialization.</summary>
    /// <param name="replacement">The complete replacement index, stable until publication completes.</param>
    /// <param name="cancellationToken">Cancellation for publication and delivery.</param>
    public Task SetResourcesAsync(IReadOnlyDictionary<string, IMessage?> replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return PublishAsync(modified =>
        {
            modified.UnionWith(resources.Keys);
            resources.Clear();
            foreach (var (name, resource) in replacement)
            {
                resources[name] = new LinearResource(name, resource, CurrentVersion);
                modified.Add(name);
            }
        }, cancellationToken);
    }

    /// <summary>Returns a fresh outer index containing the retained stable messages.</summary>
    public Dictionary<string, IMessage?> GetResources()
    {
        lock (gate)
        {
            return resources.ToDictionary(pair => pair.Key, pair => pair.Value.Resource, StringComparer.Ordinal);
        }
    }

    /// <summary>Gets the resource count without copying the index.</summary>
    public int NumResources
    {
        get
        {
            lock (gate)
            {
                return resources.Count;
            }
        }
    }

    /// <summary>Reports the upstream unsupported Fetch operation.</summary>
    /// <param name="request">The originating Fetch request.</param>
    /// <param name="cancellationToken">The caller's request cancellation.</param>
    public Task<ISotwResponse> FetchAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        // Unsupported for every request, including one whose caller has canceled.
        _ = request;
        _ = cancellationToken;
        return Task.FromException<ISotwResponse>(new NotSupportedException("Fetch is not implemented by LinearCache."));
    }

    Task<ISotwResponse> IConfigFetcher.FetchAsync(DiscoveryRequest request, ResponseContext context) =>
        FetchAsync(request, context.CancellationToken);

    private string CurrentVersion => versionPrefix + version.ToString(CultureInfo.InvariantCulture);

    private async Task PublishAsync(Action<HashSet<string>> update, CancellationToken cancellationToken)
    {
        if (!publication.Wait(0))
        {
            await publication.WaitAsync(cancellationToken);
        }
        try
        {
            var deliveries = new List<Task<bool>>();
            Exception? failure = null;
            try
            {
                lock (gate)
                {
                    version++;
                    var modified = new HashSet<string>(StringComparer.Ordinal);
                    update(modified);
                    StartDeliveries(modified, cancellationToken, deliveries);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failure = error;
            }
            try
            {
                await Task.WhenAll(deliveries);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failure ??= error;
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            publication.Release();
        }
    }
}
