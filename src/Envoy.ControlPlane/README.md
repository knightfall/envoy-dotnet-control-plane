# Envoy.ControlPlane Core

Core provides resource entries, name/type mapping, partial snapshots, EDS/RDS reference checks, explicit per-resource Delta versions, one-shot watch primitives and the SotW SnapshotCache. It references the authoritative Protocol bindings and has no hosting or optional validation/CEL dependency. Streaming services, Delta cache delivery and heartbeat scheduling remain later implementation tasks.

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

`IConfigWatcher` supports custom SotW and Delta producers. A producer uses `Watch<T>` to complete or fault one response; its optional cleanup callback removes the producer registration exactly once. Immediate completion before the consumer awaits is supported. Disposing a pending watch cancels its completion; disposing after delivery preserves the first result. Continuations run asynchronously, keeping consumer processing out of producer synchronization.

A consumer owns its watch lifetime:

```csharp
using var watch = watcher.CreateWatch(request, subscription);
var response = await watch.Completion.WaitAsync(cancellationToken);
var discoveryResponse = response.GetDiscoveryResponse();
```

The disposal scope releases the registration when the consumer exits, including a canceled wait. A custom watcher can throw during creation; a producer can fault pending delivery; response materialization can fail after delivery. `ISubscription` retains the stable exact names, prefixes, wildcard flag and returned resource versions. `ISotwResponse`/`IDeltaResponse` retain the originating request, version, returned metadata and `ResponseContext` cancellation/caller state. Actual cache selection, raw/TTL encoding and network delivery follow in later tasks.

`SnapshotCache` stores snapshots by node ID by default; applications can provide `INodeHasher` and a standard `TimeProvider` for their clock. `SetSnapshot` retains the replacement snapshot, completes eligible SotW watches and returns without waiting for proxy ACK. It invokes neither a validator nor consistency checks. No snapshot and an up-to-date subscription remain pending; an available update can complete before `CreateWatch` returns.

`GetSnapshot` throws `KeyNotFoundException` when absent. `GetStatusInfo` returns a synchronized live view of the first retained node, watch counts and request times; publication alone does not create status. `ClearSnapshot` removes indexed snapshot/status without completing or canceling old watches. Dispose watches when the consumer exits. Normal responses retain request/version/context and serialize only when materialized; TTL entries use the standard discovery-resource wrapper. Delta delivery currently throws `NotSupportedException`.

The optional ADS readiness gate preserves upstream's exact coverage rule: non-wildcard requested names must cover resources present in the snapshot. Failing that check during creation leaves an untracked pending watch. SnapshotCache follows the upstream selection path, including its prefix-only and overlapping wildcard/exact-name outcomes; repository fixtures document those limits. This does not implement the ordered ADS stream engine.

`SotwSubscription` implements the subscription contract using source-equivalent wildcard flags and exact/prefix sets. Initial empty requests retain legacy wildcard until the first explicit name, prefix or `*`; later empty requests do not reactivate it. `SotwOptions` can disable this legacy behavior globally or for ordinal type URLs, while explicit `*` remains available. `SetResourceSubscription` replaces names and cleans returned versions no longer covered by exact/prefix subscriptions. `SetReturnedResources` copies read-only response metadata into stream-owned bookkeeping, allowing a resource to be returned again after unsubscribe/re-subscribe without a snapshot version change.

Cancel or complete the current watch before changing subscription state. `MatchesResponse` checks all returned metadata names against the current exact/prefix/wildcard view, without materializing the payload; it supplies the source's obsolete-response predicate. Stream-loop integration and nonce/callback behavior remain later work. Subscription state is owned by the processing loop and adds no locks or generalized state-machine framework.
