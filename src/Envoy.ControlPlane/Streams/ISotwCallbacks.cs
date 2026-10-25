// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

/// <summary>Observes the source-ordered SotW lifecycle; hooks run serially outside cache locks.</summary>
public interface ISotwCallbacks
{
    /// <summary>Runs before request processing; rejection still causes a close notification.</summary>
    /// <param name="context">The original stream context.</param>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="typeUrl">The implied type, or empty for ADS.</param>
    /// <param name="cancellationToken">Combined call and host cancellation.</param>
    ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken);

    /// <summary>Runs after normalization and before stale-nonce filtering; may change the request.</summary>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="request">The normalized, mutable request.</param>
    /// <param name="cancellationToken">Combined call and host cancellation.</param>
    ValueTask OnStreamRequestAsync(long streamId, DiscoveryRequest request, CancellationToken cancellationToken);

    /// <summary>Runs after nonce/bookkeeping updates and before sending, with the response creation context.</summary>
    /// <param name="context">The cache response creation context.</param>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="request">The response's originating request.</param>
    /// <param name="response">The outgoing envelope, which may be changed.</param>
    /// <param name="cancellationToken">Combined call and host cancellation.</param>
    ValueTask OnStreamResponseAsync(ResponseContext context, long streamId, DiscoveryRequest request,
        DiscoveryResponse response, CancellationToken cancellationToken);

    /// <summary>Runs once after owned resources are released, including when open was rejected.</summary>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="node">The last captured node.</param>
    ValueTask OnStreamClosedAsync(long streamId, Node node);
}
