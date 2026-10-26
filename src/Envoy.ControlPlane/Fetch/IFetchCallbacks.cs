// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Fetch;

/// <summary>Observes unary Fetch in request, cache, materialization and response order.</summary>
public interface IFetchCallbacks
{
    /// <summary>Runs before cache access; may mutate the request or reject by throwing.</summary>
    /// <param name="context">The original caller context and cancellation token.</param>
    /// <param name="request">The mutable request after transport type selection.</param>
    ValueTask OnFetchRequestAsync(ResponseContext context, DiscoveryRequest request);

    /// <summary>Runs after materialization, including with null when materialization fails.</summary>
    /// <param name="request">The original request.</param>
    /// <param name="response">The mutable outgoing response, or null on materialization failure.</param>
    ValueTask OnFetchResponseAsync(DiscoveryRequest request, DiscoveryResponse? response);
}
