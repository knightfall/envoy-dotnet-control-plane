// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

internal sealed partial class SotwSession(IConfigWatcher cache, ISotwCallbacks? callbacks, SotwOptions options,
    long streamId, CancellationToken shutdown)
{
    private readonly Dictionary<string, TypeWatch> watches = new(StringComparer.Ordinal);
    private Envoy.Config.Core.V3.Node node = new();
    private IAsyncEnumerator<DiscoveryRequest>? input;
    private Task<bool>? receive;
    private long nonce;
    private int nextSelection;

    internal async Task RunAsync(IAsyncEnumerable<DiscoveryRequest> requests,
        Func<DiscoveryResponse, CancellationToken, ValueTask> send, string typeUrl, ResponseContext context)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, shutdown);
        var failures = new List<Exception>();
        try
        {
            InitializeOrderedResponses(typeUrl);
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

    private async Task ProcessAsync(Func<DiscoveryResponse, CancellationToken, ValueTask> send,
        string typeUrl, CancellationToken cancellationToken)
    {
        Task canceled = Task.Delay(Timeout.Infinite, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var candidates = new List<Task>(watches.Count + 2) { canceled, receive! };
            var pendingWatches = new List<TypeWatch>();
            if (orderedResponses is not null)
            {
                responseReady ??= orderedResponses.Reader.WaitToReadAsync(cancellationToken).AsTask();
                candidates.Add(responseReady);
            }
            else
            {
                foreach (var watch in watches.Values)
                {
                    if (watch.Pending is not null)
                    {
                        candidates.Add(watch.Pending);
                        pendingWatches.Add(watch);
                    }
                }
            }
            await Task.WhenAny(candidates);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            int selected = SelectReady(candidates);
            if (selected == 0)
            {
                return;
            }
            if (selected == 1)
            {
                if (!await receive!)
                {
                    return;
                }
                DiscoveryRequest request = input!.Current;
                // Hold at most one next request while a callback or send is awaited.
                receive = ReceiveAsync(input);
                await ProcessRequestAsync(request, typeUrl, send, cancellationToken);
            }
            else if (orderedResponses is not null)
            {
                var available = responseReady!;
                responseReady = null;
                if (!await available)
                {
                    throw new SotwProtocolException(SotwProtocolError.Unavailable, "resource channel closed");
                }
                if (orderedResponses.Reader.TryRead(out var response))
                {
                    await SendOrderedAsync(response, send, cancellationToken);
                }
            }
            else
            {
                var watch = pendingWatches[selected - 2];
                Task<bool> pending = watch.Pending!;
                watch.Pending = null;
                if (pending.IsCanceled)
                {
                    throw new SotwProtocolException(SotwProtocolError.Unavailable, "resource watch failed");
                }
                if (!await pending || !watch.Responses!.Reader.TryRead(out var response))
                {
                    throw new SotwProtocolException(SotwProtocolError.Unavailable, "resource watch closed");
                }
                if (response is null)
                {
                    throw new SotwProtocolException(SotwProtocolError.Unavailable, "empty resource response");
                }
                await SendAsync(response, send, cancellationToken);
            }
        }
    }

    private int SelectReady(IReadOnlyList<Task> candidates)
    {
        // Resume after the previous selection so buffered input cannot starve ready watches.
        for (int offset = 0; offset < candidates.Count; offset++)
        {
            int index = (nextSelection + offset) % candidates.Count;
            if (candidates[index].IsCompleted)
            {
                nextSelection = (index + 1) % candidates.Count;
                return index;
            }
        }
        throw new InvalidOperationException("No completed stream operation after wake-up");
    }

    private async ValueTask ProcessRequestAsync(DiscoveryRequest request, string typeUrl,
        Func<DiscoveryResponse, CancellationToken, ValueTask> send, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new SotwProtocolException(SotwProtocolError.Unavailable, "empty request");
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
                throw new SotwProtocolException(SotwProtocolError.InvalidArgument, "type URL is required for ADS");
            }
            request.TypeUrl = typeUrl;
        }
        string requestType = request.TypeUrl;
        if (callbacks is not null)
        {
            await callbacks.OnStreamRequestAsync(streamId, request, cancellationToken);
        }
        if (orderedResponses is null)
        {
            requestType = request.TypeUrl;
        }
        SotwSubscription subscription;
        if (watches.TryGetValue(requestType, out var previous))
        {
            if (previous.Nonce.Length > 0 && request.ResponseNonce != previous.Nonce)
            {
                return;
            }
            await previous.CloseAsync();
            await DrainOtherResponsesAsync(requestType, send, cancellationToken);
            subscription = previous.Subscription;
            subscription.SetResourceSubscription(request.ResourceNames);
        }
        else
        {
            subscription = new SotwSubscription(request.ResourceNames, options.IsLegacyWildcardActive(requestType));
        }
        var individual = orderedResponses is null ? CreateResponseChannel(1) : null;
        var registration = cache.CreateWatch(request, subscription, (orderedResponses ?? individual!).Writer)
            ?? throw new SotwProtocolException(SotwProtocolError.Unavailable, "missing resource watch");
        var current = new TypeWatch(registration, subscription, individual);
        watches[requestType] = current;
        if (individual is not null)
        {
            current.Pending = individual.Reader.WaitToReadAsync(cancellationToken).AsTask();
        }
    }

    private async ValueTask SendAsync(ISotwResponse response, Func<DiscoveryResponse, CancellationToken, ValueTask> send,
        CancellationToken cancellationToken)
    {
        var request = response.Request ?? throw new InvalidOperationException("Missing request in response");
        if (!watches.TryGetValue(request.TypeUrl, out var current))
        {
            throw new InvalidOperationException("No current watch for " + request.TypeUrl);
        }
        if (!current.Subscription.MatchesResponse(response))
        {
            return;
        }
        var materialized = response.GetDiscoveryResponse() ?? throw new InvalidOperationException("Missing discovery response");
        // Custom caches may share the envelope; generated cloning shares immutable payload bytes.
        var outgoing = materialized.Clone();
        outgoing.Nonce = (++nonce).ToString(CultureInfo.InvariantCulture);
        current.Subscription.SetReturnedResources(response.ReturnedResources);
        current.Nonce = outgoing.Nonce;
        if (callbacks is not null)
        {
            await callbacks.OnStreamResponseAsync(response.Context, streamId, request, outgoing, cancellationToken);
        }
        await send(outgoing, cancellationToken);
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
        orderedResponses?.Writer.TryComplete();
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

    private static async Task<bool> ReceiveAsync(IAsyncEnumerator<DiscoveryRequest> input)
    {
        try
        {
            return await input.MoveNextAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The Go reader closes its input channel on every receive error.
            return false;
        }
    }

    private sealed class TypeWatch(IDisposable watch, SotwSubscription subscription, Channel<ISotwResponse>? responses)
    {
        private IDisposable? registration = watch;
        internal SotwSubscription Subscription { get; } = subscription;
        internal Channel<ISotwResponse>? Responses { get; } = responses;
        internal Task<bool>? Pending { get; set; }
        internal string Nonce { get; set; } = "";

        internal async ValueTask CloseAsync()
        {
            var owned = registration;
            registration = null;
            try
            {
                if (owned is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync();
                }
                else
                {
                    owned?.Dispose();
                }
            }
            finally
            {
                Responses?.Writer.TryComplete();
                _ = Pending?.Exception;
            }
        }
    }
}
