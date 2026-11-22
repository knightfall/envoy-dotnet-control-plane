// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted from the pinned SotW heartbeat scan using .NET timer and channel ownership.
using System.Runtime.ExceptionServices;
using Envoy.ControlPlane.Resources;

namespace Envoy.ControlPlane.Cache;

public sealed partial class SnapshotCache
{
    /// <summary>Refreshes pending SotW TTL watches until canceled; the caller owns and awaits this task.</summary>
    /// <param name="interval">The positive interval supported by PeriodicTimer.</param>
    /// <param name="context">Loop cancellation and state forwarded to heartbeat responses.</param>
    /// <remarks>
    /// Uses this cache's TimeProvider. Each node has at most one outstanding refresh per loop.
    /// Stopping the loop leaves the cache and its pending watches usable.
    /// </remarks>
    public async Task RunHeartbeatsAsync(TimeSpan interval, ResponseContext context = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        using var timer = new PeriodicTimer(interval, timeProvider);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var pending = new Dictionary<string, Task>(StringComparer.Ordinal);
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            while (true)
            {
                var tick = timer.WaitForNextTickAsync(stop.Token).AsTask();
                if (await Task.WhenAny(tick, failure.Task) == failure.Task || !await tick)
                {
                    break;
                }
                foreach (var (nodeId, work) in pending)
                {
                    if (work.IsCompleted)
                    {
                        await work;
                        pending.Remove(nodeId);
                    }
                }
                foreach (string nodeId in GetHeartbeatNodes())
                {
                    if (failure.Task.IsCompleted)
                    {
                        break;
                    }
                    if (!pending.ContainsKey(nodeId))
                    {
                        var work = SendNodeHeartbeatsAsync(nodeId, context, stop.Token, failure);
                        if (!work.IsCompletedSuccessfully)
                        {
                            pending.Add(nodeId, work);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stop dispatching ticks while outstanding node writes settle.
        }
        finally
        {
            try
            {
                await stop.CancelAsync();
            }
            finally
            {
                await Task.WhenAll(pending.Values);
            }
        }
        if (failure.Task.IsCompletedSuccessfully)
        {
            ExceptionDispatchInfo.Capture(await failure.Task).Throw();
        }
    }

    private string[] GetHeartbeatNodes()
    {
        lock (gate)
        {
            return status.Where(pair => pair.Value.Watches.Count > 0 && snapshots.ContainsKey(pair.Key))
                .Select(pair => pair.Key)
                .ToArray();
        }
    }

    private async Task SendNodeHeartbeatsAsync(string nodeId, ResponseContext context, CancellationToken cancellationToken,
        TaskCompletionSource<Exception> loopFailure)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publication = await EnterPublicationAsync(nodeId, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deliveries = new List<Func<Task<bool>>>();
                Exception? failure = null;
                try
                {
                    lock (gate)
                    {
                        if (snapshots.TryGetValue(nodeId, out var snapshot) && status.TryGetValue(nodeId, out var info))
                        {
                            foreach (var watch in info.Watches.Values.ToArray())
                            {
                                if (CreateHeartbeat(snapshot, watch, context) is { } response)
                                {
                                    deliveries.Add(() => watch.Registration.DeliverAsync(response, cancellationToken));
                                }
                            }
                        }
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    failure = error;
                }
                await DeliverSelectedAsync(deliveries, failure);
            }
            finally
            {
                ExitPublication(nodeId, publication);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A stopped loop leaves the node's canceled delivery registered for retry.
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            loopFailure.TrySetResult(error);
        }
    }

    private static ISotwResponse? CreateHeartbeat(IResourceSnapshot snapshot, PendingWatch watch, ResponseContext context)
    {
        var resources = snapshot.GetResourcesAndTtl(watch.Request.TypeUrl);
        if (resources is null)
        {
            return null;
        }
        var selected = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
        if (watch.Subscription.IsWildcard)
        {
            foreach (var (name, entry) in resources)
            {
                if (entry.Ttl is not null)
                {
                    selected.Add(name, entry);
                }
            }
        }
        foreach (string name in watch.Subscription.SubscribedResources)
        {
            if (resources.TryGetValue(name, out var entry) && entry.Ttl is not null)
            {
                selected.TryAdd(name, entry);
            }
        }
        return selected.Count == 0 ? null : new RawSotwResponse(watch.Request, snapshot.GetVersion(watch.Request.TypeUrl),
            selected.Values.ToArray(), watch.Subscription.ReturnedResources, context, heartbeat: true);
    }
}
