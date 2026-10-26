// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

/// <summary>Observes the Delta lifecycle; hooks run serially outside cache locks.</summary>
public interface IDeltaCallbacks
{
    /// <summary>Runs before request processing; rejection still causes a close notification.</summary>
    /// <param name="context">The original stream context.</param>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="typeUrl">The implied type, or empty for ADS.</param>
    /// <param name="cancellationToken">Combined call and host cancellation.</param>
    ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken);

    /// <summary>Runs after queued responses drain and before node/type normalization; may change the request.</summary>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="request">The original mutable request.</param>
    /// <param name="cancellationToken">Combined call and host cancellation.</param>
    ValueTask OnStreamRequestAsync(long streamId, DeltaDiscoveryRequest request, CancellationToken cancellationToken);

    /// <summary>Runs after nonce assignment and before sending; returned versions advance only after a successful send.</summary>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="request">The response's originating request.</param>
    /// <param name="response">The outgoing envelope, which may be changed.</param>
    /// <param name="cancellationToken">Combined call and host cancellation.</param>
    ValueTask OnStreamResponseAsync(long streamId, DeltaDiscoveryRequest request,
        DeltaDiscoveryResponse response, CancellationToken cancellationToken);

    /// <summary>Runs once after owned resources are released, including when open was rejected.</summary>
    /// <param name="streamId">The server-local stream identifier.</param>
    /// <param name="node">The last captured node.</param>
    ValueTask OnStreamClosedAsync(long streamId, Node node);
}
