// Copyright 2022 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.Config.Core.V3;
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Envoy.ControlPlane.Clients;

/// <summary>Receives one SotW ADS resource family and explicitly acknowledges or rejects its responses.</summary>
/// <param name="node">The caller-owned stable node, or null.</param>
/// <param name="typeUrl">The fixed resource type; no subscription or decoding policy is added.</param>
public sealed class SotwAdsClient(Node? node, string typeUrl) : IDisposable
{
    private readonly string typeUrl = typeUrl ?? throw new ArgumentNullException(nameof(typeUrl));
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object lifetime = new();
    private AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse>? call;
    private DiscoveryResponse? received;
    private DiscoveryResponse? accepted;
    private bool disposed;

    /// <summary>Opens ADS and sends an initial ACK; reinitialization retains the source's received/accepted state.</summary>
    /// <param name="invoker">The application-owned gRPC invoker; its channel is never disposed.</param>
    /// <param name="options">Ordinary headers, credentials, deadline and stream cancellation.</param>
    /// <exception cref="ObjectDisposedException">The wrapper has been disposed.</exception>
    public async Task InitializeAsync(CallInvoker invoker, CallOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        await operations.WaitAsync(options.CancellationToken);
        try
        {
            ThrowIfDisposed();
            var replacement = new AggregatedDiscoveryService.AggregatedDiscoveryServiceClient(invoker)
                .StreamAggregatedResources(options);
            bool installed = false;
            try
            {
                AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse>? previous;
                lock (lifetime)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    previous = call;
                    call = replacement;
                    installed = true;
                }
                previous?.Dispose();
                accepted = received;
                await SendAsync(replacement, null, options.CancellationToken);
            }
            finally
            {
                if (!installed)
                {
                    replacement.Dispose();
                }
            }
        }
        finally
        {
            operations.Release();
        }
    }

    /// <summary>Waits for the next resource list; receiving alone does not advance acceptance.</summary>
    /// <param name="cancellationToken">Cancels queueing or the read; gRPC read cancellation may cancel the call.</param>
    /// <exception cref="EndOfStreamException">The server ended the response stream normally.</exception>
    /// <exception cref="InvalidOperationException">The client is uninitialized or received a null response.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper has been disposed.</exception>
    public async Task<IReadOnlyList<Any>> FetchAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            var active = GetCall();
            if (!await active.ResponseStream.MoveNext(cancellationToken))
            {
                throw new EndOfStreamException("The ADS response stream ended.");
            }
            received = active.ResponseStream.Current ?? throw new InvalidOperationException("The ADS server returned a null response.");
            return received.Resources;
        }
        finally
        {
            operations.Release();
        }
    }

    /// <summary>Accepts the last received response before attempting its ACK send, matching the source.</summary>
    /// <param name="cancellationToken">Cancellation for queueing and sending.</param>
    /// <exception cref="InvalidOperationException">The client has not been initialized.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper has been disposed.</exception>
    public async Task AckAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            var active = GetCall();
            accepted = received;
            await SendAsync(active, null, cancellationToken);
        }
        finally
        {
            operations.Release();
        }
    }

    /// <summary>Rejects the latest nonce while retaining the last accepted version.</summary>
    /// <param name="message">The rejection message; the source leaves the status code at zero.</param>
    /// <param name="cancellationToken">Cancellation for queueing and sending.</param>
    /// <exception cref="InvalidOperationException">The client has not been initialized.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper has been disposed.</exception>
    public async Task NackAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await operations.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(GetCall(), new Google.Rpc.Status { Message = message }, cancellationToken);
        }
        finally
        {
            operations.Release();
        }
    }

    /// <summary>Classifies EOF and gRPC Unavailable/Cancelled, including ordinary inner-exception wrapping.</summary>
    /// <param name="error">The observed failure, or null.</param>
    public static bool IsConnectionError(Exception? error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is EndOfStreamException || current is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.Cancelled })
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Cancels and disposes the owned call. Callers still await their pending operations.</summary>
    public void Dispose()
    {
        AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse>? previous;
        lock (lifetime)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            previous = call;
            call = null;
        }
        previous?.Dispose();
    }

    private AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse> GetCall()
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return call ?? throw new InvalidOperationException("The ADS client has not been initialized.");
        }
    }

    private void ThrowIfDisposed()
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }

    private Task SendAsync(AsyncDuplexStreamingCall<DiscoveryRequest, DiscoveryResponse> active,
        Google.Rpc.Status? error, CancellationToken cancellationToken) => active.RequestStream.WriteAsync(new DiscoveryRequest
        {
            Node = node,
            TypeUrl = typeUrl,
            VersionInfo = accepted?.VersionInfo ?? "",
            ResponseNonce = received?.Nonce ?? "",
            ErrorDetail = error,
        }, cancellationToken);
}
