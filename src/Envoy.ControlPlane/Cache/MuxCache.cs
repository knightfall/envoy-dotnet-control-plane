// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted from the pinned MuxCache behavior using separate .NET watcher/fetcher capabilities.
using System.Threading.Channels;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Routes requests to caller-owned caches without coordinating their publications.</summary>
public sealed class MuxCache : IConfigWatcher, IConfigFetcher
{
    private readonly Dictionary<string, IConfigWatcher> caches;
    private readonly Func<DiscoveryRequest, string> classify;
    private readonly Func<DeltaDiscoveryRequest, string> classifyDelta;

    /// <summary>Creates a fixed routing table with separate SotW and Delta classifiers.</summary>
    /// <param name="caches">Child caches indexed by ordinal classification keys; the index is copied.</param>
    /// <param name="classify">Selects a child for SotW watches and Fetch.</param>
    /// <param name="classifyDelta">Selects a child for Delta watches.</param>
    /// <remarks>Classifiers must support concurrent callers; the mux neither locks nor disposes its children.</remarks>
    public MuxCache(IReadOnlyDictionary<string, IConfigWatcher> caches, Func<DiscoveryRequest, string> classify,
        Func<DeltaDiscoveryRequest, string> classifyDelta)
    {
        ArgumentNullException.ThrowIfNull(caches);
        ArgumentNullException.ThrowIfNull(classify);
        ArgumentNullException.ThrowIfNull(classifyDelta);
        this.caches = new Dictionary<string, IConfigWatcher>(caches, StringComparer.Ordinal);
        this.classify = classify;
        this.classifyDelta = classifyDelta;
    }

    /// <inheritdoc/>
    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses) =>
        Select(classify(request)).CreateWatch(request, subscription, responses);

    /// <inheritdoc/>
    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses) =>
        Select(classifyDelta(request)).CreateDeltaWatch(request, subscription, responses);

    /// <inheritdoc/>
    public Task<ISotwResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default)
    {
        string key = classify(request);
        var child = Select(key);
        return child is IConfigFetcher fetcher ? fetcher.FetchAsync(request, context)
            : Task.FromException<ISotwResponse>(new NotSupportedException("Fetch is not implemented by cache " + key + "."));
    }

    private IConfigWatcher Select(string key) => caches.TryGetValue(key, out var child) ? child
        : throw new KeyNotFoundException("No cache defined for key " + key + ".");
}
