// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

/// <summary>Processes ordinary SotW streams with one receive operation and one serial send path.</summary>
public sealed class SotwServer
{
    private readonly IConfigWatcher cache;
    private readonly ISotwCallbacks? callbacks;
    private readonly SotwOptions options;
    private readonly CancellationToken shutdown;
    private long streamCount;

    /// <summary>Creates a server using a caller-owned cache and optional hooks, options and host shutdown.</summary>
    /// <param name="cache">The application-owned configuration watcher.</param>
    /// <param name="callbacks">Optional serial lifecycle hooks.</param>
    /// <param name="options">Source-equivalent subscription options.</param>
    /// <param name="shutdown">Cancellation shared by all server streams.</param>
    public SotwServer(IConfigWatcher cache, ISotwCallbacks? callbacks = null, SotwOptions? options = null,
        CancellationToken shutdown = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        this.cache = cache;
        this.callbacks = callbacks;
        this.options = options ?? new SotwOptions();
        this.shutdown = shutdown;
    }

    /// <summary>Runs a stream; an empty type URL selects unordered ADS. Inputs and hooks must honor cancellation.</summary>
    /// <param name="requests">The single-reader input, disposed when the stream ends.</param>
    /// <param name="send">The awaited, serial response writer.</param>
    /// <param name="typeUrl">The implied resource type, or an empty string for ADS.</param>
    /// <param name="context">The stream cancellation and caller state passed to open.</param>
    public Task StreamAsync(IAsyncEnumerable<DiscoveryRequest> requests,
        Func<DiscoveryResponse, CancellationToken, ValueTask> send, string typeUrl, ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(typeUrl);
        return new SotwSession(cache, callbacks, options, Interlocked.Increment(ref streamCount), shutdown)
            .RunAsync(requests, send, typeUrl, context);
    }
}
