// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using System.Runtime.ExceptionServices;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Fetch;

/// <summary>Shares cache polling and callback semantics across Fetch transports.</summary>
/// <param name="cache">The application-owned polling producer.</param>
/// <param name="callbacks">Optional application Fetch callbacks.</param>
public sealed class FetchServer(IConfigFetcher cache, IFetchCallbacks? callbacks = null)
{
    /// <summary>Fetches and materializes a response, preserving original producer failures.</summary>
    /// <param name="request">The mutable originating request.</param>
    /// <param name="context">The caller context passed to the request hook and producer.</param>
    /// <exception cref="InvalidOperationException">The producer returned no response envelope.</exception>
    public async Task<DiscoveryResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (callbacks is not null)
        {
            await callbacks.OnFetchRequestAsync(context, request);
        }
        var raw = await cache.FetchAsync(request, context);
        if (raw is null)
        {
            throw new InvalidOperationException("Missing response.");
        }
        DiscoveryResponse? response = null;
        Exception? failure = null;
        try
        {
            response = raw.GetDiscoveryResponse();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failure = error;
        }
        if (callbacks is not null)
        {
            await callbacks.OnFetchResponseAsync(request, response);
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return response!;
    }
}
