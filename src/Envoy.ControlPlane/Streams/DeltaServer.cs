// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

/// <summary>Processes per-type and ADS Delta streams with one shared response channel and serial sends.</summary>
public sealed class DeltaServer
{
    private readonly IConfigWatcher cache;
    private readonly IDeltaCallbacks? callbacks;
    private readonly DeltaOptions options;
    private readonly CancellationToken shutdown;
    private long streamCount;

    /// <summary>Creates a server using an application-owned cache and optional hooks, options and host shutdown.</summary>
    /// <param name="cache">The application-owned configuration watcher.</param>
    /// <param name="callbacks">Optional serial lifecycle hooks.</param>
    /// <param name="options">Source-equivalent legacy wildcard settings.</param>
    /// <param name="shutdown">Cancellation shared by all server streams.</param>
    public DeltaServer(IConfigWatcher cache, IDeltaCallbacks? callbacks = null, DeltaOptions? options = null,
        CancellationToken shutdown = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        this.cache = cache;
        this.callbacks = callbacks;
        this.options = options ?? new DeltaOptions();
        this.shutdown = shutdown;
    }

    /// <summary>Runs a Delta stream; an empty type URL selects ADS. Inputs and hooks must honor cancellation.</summary>
    /// <param name="requests">The single-reader input, disposed when the stream ends.</param>
    /// <param name="send">The awaited, serial response writer.</param>
    /// <param name="typeUrl">The implied resource type, or an empty string for ADS.</param>
    /// <param name="context">The original stream cancellation and caller state passed to open.</param>
    public Task StreamAsync(IAsyncEnumerable<DeltaDiscoveryRequest> requests,
        Func<DeltaDiscoveryResponse, CancellationToken, ValueTask> send, string typeUrl, ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(typeUrl);
        return new DeltaSession(cache, callbacks, options, Interlocked.Increment(ref streamCount), shutdown)
            .RunAsync(requests, send, typeUrl, context);
    }
}
