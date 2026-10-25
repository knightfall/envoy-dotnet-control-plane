// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using System.Globalization;
using System.Runtime.ExceptionServices;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

internal sealed class SotwSession(IConfigWatcher cache, ISotwCallbacks? callbacks, SotwOptions options,
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
            foreach (var watch in watches.Values)
            {
                if (watch.Pending is not null)
                {
                    candidates.Add(watch.Pending);
                }
            }
            await Task.WhenAny(candidates);
            Task ready = SelectReady(candidates);
            if (ready == canceled)
            {
                return;
            }
            if (ready == receive)
            {
                if (!await receive!)
                {
                    return;
                }
                DiscoveryRequest request = input!.Current;
                // Hold at most one next request while a callback or send is awaited.
                receive = ReceiveAsync(input);
                await ProcessRequestAsync(request, typeUrl, cancellationToken);
            }
            else
            {
                var watch = watches.Values.First(value => value.Pending == ready);
                Task<ISotwResponse> pending = watch.Pending!;
                watch.Pending = null;
                if (pending.IsCanceled)
                {
                    throw new SotwProtocolException(SotwProtocolError.Unavailable, "resource watch failed");
                }
                var response = await pending;
                if (response is null)
                {
                    throw new SotwProtocolException(SotwProtocolError.Unavailable, "empty resource response");
                }
                await SendAsync(response, send, cancellationToken);
            }
        }
    }

    private Task SelectReady(IReadOnlyList<Task> candidates)
    {
        // Resume after the previous selection so buffered input cannot starve ready watches.
        for (int offset = 0; offset < candidates.Count; offset++)
        {
            int index = (nextSelection + offset) % candidates.Count;
            if (candidates[index].IsCompleted)
            {
                nextSelection = (index + 1) % candidates.Count;
                return candidates[index];
            }
        }
        throw new InvalidOperationException("No completed stream operation after wake-up");
    }

    private async ValueTask ProcessRequestAsync(DiscoveryRequest request, string typeUrl, CancellationToken cancellationToken)
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
        if (callbacks is not null)
        {
            await callbacks.OnStreamRequestAsync(streamId, request, cancellationToken);
        }
        SotwSubscription subscription;
        if (watches.TryGetValue(request.TypeUrl, out var previous))
        {
            if (previous.Nonce.Length > 0 && request.ResponseNonce != previous.Nonce)
            {
                return;
            }
            previous.Close();
            subscription = previous.Subscription;
            subscription.SetResourceSubscription(request.ResourceNames);
        }
        else
        {
            subscription = new SotwSubscription(request.ResourceNames, options.IsLegacyWildcardActive(request.TypeUrl));
        }
        var registration = cache.CreateWatch(request, subscription)
            ?? throw new SotwProtocolException(SotwProtocolError.Unavailable, "missing resource watch");
        var current = new TypeWatch(registration, subscription);
        watches[request.TypeUrl] = current;
        current.Pending = registration.Completion ?? throw new InvalidOperationException("Missing watch completion");
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
                watch.Close();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add(error);
            }
        }
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

    private sealed class TypeWatch(IWatch<ISotwResponse> watch, SotwSubscription subscription)
    {
        private IWatch<ISotwResponse>? registration = watch;
        internal SotwSubscription Subscription { get; } = subscription;
        internal Task<ISotwResponse>? Pending { get; set; }
        internal string Nonce { get; set; } = "";

        internal void Close()
        {
            var owned = registration;
            registration = null;
            owned?.Dispose();
        }
    }
}
