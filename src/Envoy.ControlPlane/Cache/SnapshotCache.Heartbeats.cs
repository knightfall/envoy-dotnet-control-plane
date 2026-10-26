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
    /// <remarks>Uses this cache's TimeProvider. Stopping the loop leaves the cache and its pending watches usable.</remarks>
    public async Task RunHeartbeatsAsync(TimeSpan interval, ResponseContext context = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(context.CancellationToken))
            {
                await SendHeartbeatsAsync(context);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Awaiting the loop settles any in-flight writes before the timer is released.
        }
    }

    private async Task SendHeartbeatsAsync(ResponseContext context)
    {
        await publication.WaitAsync(context.CancellationToken);
        try
        {
            var deliveries = new List<Task<bool>>();
            Exception? failure = null;
            try
            {
                lock (gate)
                {
                    foreach (var (nodeId, info) in status)
                    {
                        if (!snapshots.TryGetValue(nodeId, out var snapshot))
                        {
                            continue;
                        }
                        foreach (var watch in info.Watches.Values.ToArray())
                        {
                            if (CreateHeartbeat(snapshot, watch, context) is { } response)
                            {
                                deliveries.Add(watch.Registration.DeliverAsync(response, context.CancellationToken));
                            }
                        }
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failure = error;
            }
            try
            {
                await Task.WhenAll(deliveries);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failure ??= error;
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            publication.Release();
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
