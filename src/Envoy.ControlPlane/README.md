# Envoy.ControlPlane Core

Core provides resource entries, name/type mapping, partial snapshots, EDS/RDS reference checks, explicit per-resource Delta versions, one-shot watches, the SotW SnapshotCache, subscription state and transport-independent stream processing with optional ordered ADS. It references the authoritative Protocol bindings and has no hosting or optional validation/CEL dependency. The separate ASP.NET Core package supplies preview SotW service adapters. Delta cache delivery and heartbeat scheduling remain later implementation tasks.

```csharp
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;

var listener = new Listener { Name = "listener", StatPrefix = "http" };
var snapshot = new Snapshot("config/v1", new Dictionary<string, IEnumerable<ResourceEntry>>
{
    [ResourceTypeUrls.Listener] = [new(listener, TimeSpan.FromSeconds(30))],
});

snapshot.EnsureConsistent(); // Optional EDS/RDS reference-set check.
snapshot.ConstructVersionMap(); // Explicit, cached Delta hashes.
string version = snapshot.GetVersion(ResourceTypeUrls.Listener);
string hash = snapshot.GetVersionMap(ResourceTypeUrls.Listener)!["listener"];
```

`ResourceCollection` indexes names with the last duplicate winning. Endpoint assignments use `ClusterName`; other generated messages use their protobuf string `name` field. `INamedResource` supports application naming. The built-in snapshot supports twelve known resource families; applications can implement `IResourceSnapshot` for arbitrary type URLs and use `Google.Protobuf.Reflection.TypeRegistry` for custom Any descriptors.

Messages must remain stable while retained. Publish replacement messages and snapshots when configuration changes. The library does not clone messages or guarantee mutation isolation. `GetResources` creates a fresh outer name index; `GetResourcesAndTtl` exposes the retained index through a read-only interface. Keep both retained messages and collection contents stable.

Type versions are caller-supplied opaque strings, compared for equality. An absent group returns a null resource index and empty version; an explicitly empty group retains its supplied version. The collection-based constructor supports independent versions per family. `GetVersionMap` returns null until `ConstructVersionMap` is called. Hashes use SHA256 over deterministic protobuf bytes; literal equality with Go hashes is not guaranteed across protobuf runtimes. TTLs and opaque type versions do not affect resource hashes.

Construction neither validates protobuf rules nor checks consistency. `EnsureConsistent` checks exact EDS/RDS resource-name sets, including listener default/standalone chains and scoped-route references. It does not establish Envoy runtime acceptance. Optional TTLs preserve absent versus zero values; .NET `TimeSpan` has 100 ns precision.

Behavior derives from the pinned upstream resource/cache implementation recorded in `upstream-baseline.json`. The package includes the upstream Apache-2.0 license. Repository evidence and source links are in `docs/plan/T05-RESOURCES-AND-SNAPSHOTS.md`.

`IConfigWatcher.CreateWatch` registers one SotW response with a caller-owned `ChannelWriter<ISotwResponse>` and returns an `IDisposable` cancellation registration. Producers establish order by writing directly to that destination; both stream modes consume channels directly. Await buffer space outside cache synchronization. Releasing one registration must leave the shared writer open. Implement `IAsyncDisposable` when delivery owns pending asynchronous writes: disposal must settle them before the stream changes subscription state. The stream discovers and awaits that interface. Delta retains its task-based contract until its implementation task.

A consumer owns its watch lifetime:

```csharp
using var watch = watcher.CreateWatch(request, subscription);
var response = await watch.Completion.WaitAsync(cancellationToken);
var discoveryResponse = response.GetDiscoveryResponse();
```

The two-argument extension shown above is an await-one convenience adapter over an individual channel. Disposing it cancels a pending read; disposing after delivery preserves the first result. Await completion or cancellation rather than assuming that task continuations have run when a producer returns. `Watch<T>` remains a standalone one-shot primitive. A custom watcher can throw during creation; response materialization can fail after delivery. `ISubscription` retains stable exact names, prefixes, wildcard state and returned versions. Response interfaces retain the originating request, version, returned metadata and `ResponseContext` cancellation/caller state.

`SnapshotCache` stores snapshots by node ID by default; applications can provide `INodeHasher` and a standard `TimeProvider` for their clock. `await cache.SetSnapshotAsync(nodeId, snapshot)` retains the replacement, queues eligible responses and waits for destination capacity without holding cache synchronization. Concurrent publications are serialized so they cannot overtake an earlier blocked publication. A queued publication can cancel before it begins and leaves the retained snapshot unchanged; the new asynchronous wait honors cancellation instead of waiting on Go-style mutex acquisition. It waits for enqueueing, without waiting for proxy ACK, and invokes neither a validator nor consistency checks. No snapshot and an up-to-date subscription remain pending; an available update can be queued before `CreateWatch` returns. A publication cancellation can leave the new snapshot installed, matching the source; eligible undelivered watches remain available for retry or disposal.

`GetSnapshot` throws `KeyNotFoundException` when absent. `GetStatusInfo` returns a synchronized live view of the first retained node, watch counts and request times; publication alone does not create status. `ClearSnapshot` removes indexed snapshot/status without completing or canceling old watches. Dispose watches when the consumer exits. Normal responses retain request/version/context and serialize only when materialized; TTL entries use the standard discovery-resource wrapper. Delta delivery currently throws `NotSupportedException`.

The optional `new SnapshotCache(ads: true)` readiness gate preserves upstream's exact coverage rule: non-wildcard requested names must cover resources present in the snapshot. Failing that check during creation leaves an untracked pending registration. It also starts eligible publication writes in known-resource priority order. SnapshotCache follows the upstream selection path, including its prefix-only and overlapping wildcard/exact-name outcomes; repository fixtures document those limits.

`SotwSubscription` implements the subscription contract using source-equivalent wildcard flags and exact/prefix sets. Initial empty requests retain legacy wildcard until the first explicit name, prefix or `*`; later empty requests do not reactivate it. `SotwOptions` can disable this legacy behavior globally or for ordinal type URLs, while explicit `*` remains available. `SetResourceSubscription` replaces names and cleans returned versions no longer covered by exact/prefix subscriptions. `SetReturnedResources` copies read-only response metadata into stream-owned bookkeeping, allowing a resource to be returned again after unsubscribe/re-subscribe without a snapshot version change.

Cancel or complete the current watch before changing subscription state. `MatchesResponse` checks all returned metadata names against the current exact/prefix/wildcard view, without materializing the payload; it supplies the source's obsolete-response predicate. `SotwServer` integrates this predicate into the processing loop. Subscription state is owned by the processing loop and adds no locks or generalized state-machine framework.

`SotwServer.StreamAsync` accepts an asynchronous request sequence, a serial asynchronous response writer, an implied type URL (empty for ADS) and stream `ResponseContext`. `new SotwOptions { OrderedAds = true }` selects a shared bounded FIFO only for ADS; the default uses an individual channel per watch. Cache readiness and stream ordering are independent options. Ordered replacement cancels the old registration, drains queued other-type responses and skips the superseded current type before changing its subscription. Returned-name filtering rejects late unsubscribed responses before materializing them. Queue capacity is twelve, matching the source; arbitrary type URLs use dictionary state and can exceed that count. Buffering waits rather than silently dropping responses. This does not establish an atomic cross-type transaction.

The server carries Node values, preserves callback order, replaces one-shot watches and assigns stream-wide nonces. `ISotwCallbacks` exposes optional asynchronous lifecycle hooks. Stale requests remain visible to request callbacks; NACKs do not trigger rollback. Ordered ADS captures the dispatch type before the request callback, while ordinary mode uses the effective type afterward, matching the source. Response hooks receive the cache creation context and an isolated envelope whose immutable payload bytes remain shared.

Call and host cancellation stop the owned input and release watches; inputs, writers and hooks must cooperate with cancellation. Close runs once, including after rejected open. EOF/receive errors end normally. Protocol failures use `SotwProtocolException`; ordinary operation failures retain their original exceptions. Ordered main-queue response failures use `Unavailable` with the original exception retained as the inner cause; failures while draining retain their original exception, matching the source. Multiple processing/cleanup failures are aggregated with the processing error first. No discovery endpoint is registered by Core; see the repository T09/T10 evidence for acceptance and hosting boundaries.
