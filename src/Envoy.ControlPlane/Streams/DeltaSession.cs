// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

internal sealed class DeltaSession(IConfigWatcher cache, IDeltaCallbacks? callbacks, DeltaOptions options,
    long streamId, CancellationToken shutdown)
{
    private readonly Dictionary<string, TypeWatch> watches = new(StringComparer.Ordinal);
    private readonly Channel<IDeltaResponse> responses = Channel.CreateBounded<IDeltaResponse>(new BoundedChannelOptions(2 * (int)ResponseType.Unknown)
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private Node node = new();
    private IAsyncEnumerator<DeltaDiscoveryRequest>? input;
    private Task<bool>? receive;
    private Task<bool>? responseReady;
    private long nonce;

    internal async Task RunAsync(IAsyncEnumerable<DeltaDiscoveryRequest> requests,
        Func<DeltaDiscoveryResponse, CancellationToken, ValueTask> send, string typeUrl, ResponseContext context)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, shutdown);
        var failures = new List<Exception>();
        try
        {
            input = requests.GetAsyncEnumerator(cancellation.Token);
            receive = ReceiveAsync(input);
            if (callbacks is not null)
            {
                await callbacks.OnStreamOpenAsync(context, streamId, typeUrl, cancellation.Token);
            }
            await ProcessAsync(send, typeUrl, cancellation.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failures.Add(error);
        }
        await CleanupAsync(cancellation, failures);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private async Task ProcessAsync(Func<DeltaDiscoveryResponse, CancellationToken, ValueTask> send,
        string typeUrl, CancellationToken cancellationToken)
    {
        Task canceled = Task.Delay(Timeout.Infinite, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            responseReady ??= responses.Reader.WaitToReadAsync(cancellationToken).AsTask();
            await Task.WhenAny(canceled, receive!, responseReady);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (receive!.IsCompleted)
            {
                if (!await receive)
                {
                    return;
                }
                var request = input!.Current;
                // Hold at most one next request while callbacks or sends are awaited.
                receive = ReceiveAsync(input);
                await ProcessRequestAsync(request, typeUrl, send, cancellationToken);
            }
            else
            {
                var ready = responseReady;
                responseReady = null;
                if (!await ready)
                {
                    throw new DeltaProtocolException(DeltaProtocolError.Unavailable, "resource channel closed");
                }
                await DrainResponsesAsync(send, cancellationToken);
            }
        }
    }

    private async ValueTask ProcessRequestAsync(DeltaDiscoveryRequest request, string typeUrl,
        Func<DeltaDiscoveryResponse, CancellationToken, ValueTask> send, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new DeltaProtocolException(DeltaProtocolError.Unavailable, "empty request");
        }
        await DrainResponsesAsync(send, cancellationToken);
        if (callbacks is not null)
        {
            await callbacks.OnStreamRequestAsync(streamId, request, cancellationToken);
        }
        if (request.Node is not null)
        {
            node = request.Node;
        }
        else
        {
            request.Node = node;
        }
        if (request.TypeUrl.Length == 0)
        {
            if (typeUrl.Length == 0)
            {
                throw new DeltaProtocolException(DeltaProtocolError.InvalidArgument, "type URL is required for ADS");
            }
            request.TypeUrl = typeUrl;
        }
        string requestType = request.TypeUrl;
        if (!watches.TryGetValue(requestType, out var current))
        {
            current = new TypeWatch(new DeltaSubscription(request.ResourceNamesSubscribe, request.ResourceNamesUnsubscribe,
                request.InitialResourceVersions, options.IsLegacyWildcardActive(requestType)));
            watches.Add(requestType, current);
        }
        else
        {
            await current.CloseAsync();
            current.Subscription.UpdateResourceSubscriptions(request.ResourceNamesSubscribe, request.ResourceNamesUnsubscribe);
        }
        current.Registration = cache.CreateDeltaWatch(request, current.Subscription, responses.Writer)
            ?? throw new DeltaProtocolException(DeltaProtocolError.Unavailable, "missing resource watch");
    }

    private async ValueTask DrainResponsesAsync(Func<DeltaDiscoveryResponse, CancellationToken, ValueTask> send,
        CancellationToken cancellationToken)
    {
        while (responses.Reader.TryRead(out var response))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendAsync(response, send, cancellationToken);
        }
    }

    private async ValueTask SendAsync(IDeltaResponse response,
        Func<DeltaDiscoveryResponse, CancellationToken, ValueTask> send, CancellationToken cancellationToken)
    {
        if (response is null)
        {
            throw new DeltaProtocolException(DeltaProtocolError.Unavailable, "empty resource response");
        }
        var request = response.Request ?? throw new InvalidOperationException("Missing request in response");
        if (!watches.TryGetValue(request.TypeUrl, out var current))
        {
            throw new InvalidOperationException("No current watch for " + request.TypeUrl);
        }
        var materialized = response.GetDeltaDiscoveryResponse() ?? throw new InvalidOperationException("Missing Delta response");
        // Custom caches may retain the envelope; each send owns its mutable metadata.
        var outgoing = materialized.Clone();
        outgoing.Nonce = (++nonce).ToString(CultureInfo.InvariantCulture);
        if (callbacks is not null)
        {
            await callbacks.OnStreamResponseAsync(streamId, request, outgoing, cancellationToken);
        }
        await send(outgoing, cancellationToken);
        current.Subscription.SetReturnedResources(response.ReturnedResources);
    }

    private async Task CleanupAsync(CancellationTokenSource cancellation, List<Exception> failures)
    {
        try
        {
            await cancellation.CancelAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failures.Add(error);
        }
        foreach (var watch in watches.Values)
        {
            try
            {
                await watch.CloseAsync();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add(error);
            }
        }
        responses.Writer.TryComplete();
        _ = responseReady?.Exception;
        if (receive is not null)
        {
            await receive;
        }
        if (input is not null)
        {
            try
            {
                await input.DisposeAsync();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add(error);
            }
        }
        if (callbacks is not null)
        {
            try
            {
                await callbacks.OnStreamClosedAsync(streamId, node);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add(error);
            }
        }
    }

    private static async Task<bool> ReceiveAsync(IAsyncEnumerator<DeltaDiscoveryRequest> input)
    {
        try
        {
            return await input.MoveNextAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The pinned Go receiver closes the input on every receive error.
            return false;
        }
    }

    private sealed class TypeWatch(DeltaSubscription subscription)
    {
        internal DeltaSubscription Subscription { get; } = subscription;
        internal IDisposable? Registration { get; set; }

        internal async ValueTask CloseAsync()
        {
            var owned = Registration;
            Registration = null;
            if (owned is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
            else
            {
                owned?.Dispose();
            }
        }
    }
}
