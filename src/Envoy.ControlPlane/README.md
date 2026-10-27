# Envoy.ControlPlane Core

NuGet candidate: `Knightfall.Envoy.ControlPlane` 1.0.0-rc.1 (root candidate). The assembly remains `Envoy.ControlPlane`.

Core provides resource entries, name/type mapping, partial snapshots, EDS/RDS reference checks, explicit per-resource Delta versions, one-shot watches, SotW/Delta SnapshotCache responses, subscription state and transport-independent stream processing with optional ordered SotW ADS. It references the authoritative Protocol bindings and has no hosting or optional validation/CEL dependency. The separate ASP.NET Core package supplies ten SotW, eleven Delta and nine unary Fetch service methods. Explicit caller-owned SotW heartbeat scheduling is available.

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

`IConfigWatcher.CreateWatch` registers one SotW response with a caller-owned `ChannelWriter<ISotwResponse>` and returns an `IDisposable` cancellation registration. Producers establish order by writing directly to that destination; both stream modes consume channels directly. Await buffer space outside cache synchronization. Releasing one registration must leave the shared writer open. Implement `IAsyncDisposable` when delivery owns pending asynchronous writes: disposal must settle them before the stream changes subscription state. The stream discovers and awaits that interface. Delta now uses the same channel-primary contract through `CreateDeltaWatch(request, subscription, writer)`; its two-argument extension is an await-one adapter. Custom producers must implement the writer overload.

A consumer owns its watch lifetime:

```csharp
using var watch = watcher.CreateWatch(request, subscription);
var response = await watch.Completion.WaitAsync(cancellationToken);
var discoveryResponse = response.GetDiscoveryResponse();
```

The two-argument extension shown above is an await-one convenience adapter over an individual channel. Disposing it cancels a pending read; disposing after delivery preserves the first result. Await completion or cancellation rather than assuming that task continuations have run when a producer returns. `Watch<T>` remains a standalone one-shot primitive. A custom watcher can throw during creation; response materialization can fail after delivery. `ISubscription` retains stable exact names, prefixes, wildcard state and returned versions. Response interfaces retain the originating request, version, returned metadata and `ResponseContext` cancellation/caller state.

`SnapshotCache` stores snapshots by node ID by default; applications can provide `INodeHasher` and a standard `TimeProvider` for their clock. `await cache.SetSnapshotAsync(nodeId, snapshot)` retains the replacement, queues eligible responses and waits for destination capacity without holding cache synchronization. Concurrent publications are serialized so they cannot overtake an earlier blocked publication. A queued publication can cancel before it begins and leaves the retained snapshot unchanged; the new asynchronous wait honors cancellation instead of waiting on Go-style mutex acquisition. It waits for enqueueing, without waiting for proxy ACK, and invokes neither a validator nor consistency checks. No snapshot and an up-to-date subscription remain pending; an available update can be queued before `CreateWatch` returns. A publication cancellation can leave the new snapshot installed, matching the source; eligible undelivered watches remain available for retry or disposal.

`GetSnapshot` throws `KeyNotFoundException` when absent. `GetStatusInfo` returns a synchronized live view of the first retained node, watch counts and request times; publication alone does not create status. `ClearSnapshot` removes indexed snapshot/status without completing or canceling old watches. Dispose watches when the consumer exits. Normal responses retain request/version/context and serialize only when materialized; TTL entries use the standard discovery-resource wrapper. Delta cache watches have separate counts/request times and select changes/removals from per-resource versions. `DeltaServer` consumes these responses; the separate ASP.NET Core package supplies Delta gRPC delivery.

The optional `new SnapshotCache(ads: true)` readiness gate preserves upstream's exact coverage rule: non-wildcard requested names must cover resources present in the snapshot. Failing that check during creation leaves an untracked pending registration. It also starts eligible publication writes in known-resource priority order. SnapshotCache follows the upstream selection path, including its prefix-only and overlapping wildcard/exact-name outcomes; repository fixtures document those limits.

`SotwSubscription` implements the subscription contract using source-equivalent wildcard flags and exact/prefix sets. Initial empty requests retain legacy wildcard until the first explicit name, prefix or `*`; later empty requests do not reactivate it. `SotwOptions` can disable this legacy behavior globally or for ordinal type URLs, while explicit `*` remains available. `SetResourceSubscription` replaces names and cleans returned versions no longer covered by exact/prefix subscriptions. `SetReturnedResources` copies read-only response metadata into stream-owned bookkeeping, allowing a resource to be returned again after unsubscribe/re-subscribe without a snapshot version change.

Cancel or complete the current watch before changing subscription state. `MatchesResponse` checks all returned metadata names against the current exact/prefix/wildcard view, without materializing the payload; it supplies the source's obsolete-response predicate. `SotwServer` integrates this predicate into the processing loop. Subscription state is owned by the processing loop and adds no locks or generalized state-machine framework.

`SotwServer.StreamAsync` accepts an asynchronous request sequence, a serial asynchronous response writer, an implied type URL (empty for ADS) and stream `ResponseContext`. `new SotwOptions { OrderedAds = true }` selects a shared bounded FIFO only for ADS; the default uses an individual channel per watch. Cache readiness and stream ordering are independent options. Ordered replacement cancels the old registration, drains queued other-type responses and skips the superseded current type before changing its subscription. Returned-name filtering rejects late unsubscribed responses before materializing them. Queue capacity is twelve, matching the source; arbitrary type URLs use dictionary state and can exceed that count. Buffering waits rather than silently dropping responses. This does not establish an atomic cross-type transaction.

The server carries Node values, preserves callback order, replaces one-shot watches and assigns stream-wide nonces. `ISotwCallbacks` exposes optional asynchronous lifecycle hooks. Stale requests remain visible to request callbacks; NACKs do not trigger rollback. Ordered ADS captures the dispatch type before the request callback, while ordinary mode uses the effective type afterward, matching the source. Response hooks receive the cache creation context and an isolated envelope whose immutable payload bytes remain shared.

Call and host cancellation stop the owned input and release watches; inputs, writers and hooks must cooperate with cancellation. Close runs once, including after rejected open. EOF/receive errors end normally. Protocol failures use `SotwProtocolException`; ordinary operation failures retain their original exceptions. Ordered main-queue response failures use `Unavailable` with the original exception retained as the inner cause; failures while draining retain their original exception, matching the source. Multiple processing/cleanup failures are aggregated with the processing error first. No discovery endpoint is registered by Core; see the repository T09/T10 evidence for acceptance and hosting boundaries.

`WellKnownFilterNames` retains the HCM and router aliases used by the original preview HTTP graph. `WellKnownNames` now supplies the complete selected conventional-name inventory; see the conversion/helper section below. Extension identity comes from the typed-config protobuf URL. The upstream well-known-name package is deprecated. Continue using generated descriptors/`Any` for typed extensions.

`DeltaSubscription` supplies stream-owned incremental exact/prefix/wildcard and returned-version state. Construct it once for a new type with initial resource versions; later `UpdateResourceSubscriptions` calls apply additions before removals without re-seeding. `SetReturnedResources` replaces owned metadata. Read-only collection/dictionary inputs fit generated requests and preserve caller ownership. Legacy unsubscribe-only behavior, wildcard/name removal markers and prefix metadata retention match the pinned source. `DeltaServer` now owns initialization and updates; these primitives do not register an endpoint. See `docs/plan/T13-DELTA-SUBSCRIPTIONS.md` for source traces and initialization boundaries.

The first T14 increment adds lazy `RawDeltaResponse` cache materialization and public `PassthroughDeltaResponse` for caller-owned envelopes/next-version metadata. Version maps are constructed only when Delta watches require them; system-version-only changes suppress unchanged payloads. The pinned SnapshotCache Delta path does not select prefixes and its raw Delta envelope omits TTL. These limits, channel migration, cancellation behavior and fourteen actual Go comparisons are documented in `docs/plan/T14-DELTA-CACHE.md`.

`DeltaServer.StreamAsync` accepts an asynchronous Delta input and serial writer with the same transport-independent ownership pattern as SotW. Its shared channel drains before each request callback. Delta callbacks see requests before node/type normalization; first versions initialize each type once, and returned versions advance after successful sends. Old nonces and NACKs remain callback-visible and update subscriptions without SotW stale filtering. `DeltaOptions` controls legacy wildcard globally or per type. `IDeltaCallbacks` supplies serial asynchronous hooks; `DeltaProtocolException` classifies protocol failures. See `docs/plan/T14-DELTA-STREAMS.md` for ten actual pinned Go traces, exact native assertions and the separately documented adapter/network evidence.

## LinearCache

`LinearCache` manages one opaque type URL independently of nodes. Its constructor accepts an optional initial named-message index and version prefix. `UpdateResourceAsync`, `DeleteResourceAsync`, `UpdateResourcesAsync` and `SetResourcesAsync` publish asynchronous updates; keep input collections stable until each call completes and retain immutable message ownership until replacement. `GetResources()` copies only the outer index.

Both watch methods implement the existing channel-primary `IConfigWatcher` contract; the same await-one extension methods remain available. Exact/prefix/wildcard indexes avoid scanning unrelated watches and deduplicate delivery for overlapping subscriptions. SotW resource versions use update counters; Delta uses shared lazy payload hashes. Identical payload updates can therefore wake SotW while Delta stays pending. CDS/LDS return full matched SotW state; other types suppress later deletion-only replies.

`NumResources` and the watch-count APIs expose synchronized diagnostics. `NumWatches(name)` sums matching index entries; `NumCacheWatches` counts distinct registrations. Disposal is idempotent and never closes the caller's writer. `FetchAsync` faults with `NotSupportedException`. See `docs/plan/T15-LINEAR-CACHE.md` in the repository for version-prefix/no-op behavior, exact source evidence and the intentional Go cancellation-counter difference. Mux routing and shared Fetch processing are documented below.

## MuxCache

`MuxCache` accepts an indexed set of caller-owned `IConfigWatcher` children and separate SotW/Delta classifiers. Fetch uses the SotW classifier. The routing index is copied with ordinal keys; classifiers must support concurrent callers. Watch calls pass the original request, subscription and channel writer and return the child's cancellation registration directly. Children retain their own publication coordination and lifetime.

`IConfigFetcher.FetchAsync(request, ResponseContext)` is an optional polling capability, separate from watching. The mux returns the selected child's task and preserves cancellation/state, responses and errors. Missing keys throw `KeyNotFoundException`; a watcher-only child reports unsupported Fetch. LinearCache implements the capability with its existing unsupported result and retains its token-based convenience method. SnapshotCache and the shared Fetch engine implement polling; the separate hosting package supplies gRPC and opt-in HTTP transports. See `docs/plan/T16-MUX-CACHE.md` in the repository for the source/native mapping and fixed-table API difference.

## TTL heartbeats

`SnapshotCache.RunHeartbeatsAsync(interval, ResponseContext)` runs a caller-owned `PeriodicTimer` using the cache's TimeProvider. Start one loop for the cache, cancel its context and await the task during shutdown. The timer is disposed and the cache remains usable. The loop is opt-in, and runtime Core adds no timer/hosting package dependency.

Heartbeats select pending SotW wildcard/exact TTL resources, retaining versions/returned metadata while omitting payloads and their serialization. A successful send releases the one-shot watch. Prefix-only selection and automatic Delta heartbeats are omitted at the pinned source revision; raw Delta TTL remains omitted, while prebuilt pass-through Delta envelopes retain caller fields. No cache-side expiry/deletion occurs. See `docs/plan/T17-TTL-HEARTBEATS.md` for lifecycle, interval constraints and source/native evidence. `docs/plan/T23-ROOT-ENVOY.md` records explicit caller-supplied Delta TTL refresh/expiry and the default released Envoy SotW CDS heartbeat limitation; the built-in SotW loop is not advertised as interoperable for that configuration.

## Fetch

`SnapshotCache` implements `IConfigFetcher`. `FetchAsync` hashes the request node and returns all resources for an empty name list, or literal exact names in request order, including duplicates. It ignores missing names; `*` and collection prefixes have no special selection meaning. Streaming ADS readiness and CDS/LDS full-state behavior do not alter polling. A missing node throws `KeyNotFoundException`; equal opaque versions throw `SkipFetchException`. Absent types have the empty version, so an empty requested version can also skip.

Fetch creates no watch/status entries or request timestamps. Its lazy response retains the original request/context and selected stable resources, uses normal TTL wrappers and exposes an empty returned-resource ledger. As in the source, the cache forwards cancellation context without rejecting an already-canceled immediate read. Application callbacks/producers may observe cancellation.

`FetchServer` consumes any `IConfigFetcher` and optional `IFetchCallbacks`. It calls the request hook, producer, materializer and response hook in that order. Request hooks may mutate or reject; response hooks may mutate the outgoing envelope and receive null if materialization failed. Missing outer envelopes fail before response hooks. Producer/materializer exceptions propagate unchanged after the applicable hooks. The original response callback signature has no context/cancellation argument. See `docs/plan/T18-FETCH-GATEWAY.md` for source mapping and transport boundaries.

## Focused SotW ADS client

`Envoy.ControlPlane.Clients.SotwAdsClient` uses the generated ADS client with an application-owned `Grpc.Core.CallInvoker`. It fixes one node/type, opens/sends its initial request explicitly, returns opaque resource lists and lets the application ACK or NACK. It adds no transport/channel constructor, reconnection, resource decoding or validation policy.

```csharp
using var client = new Envoy.ControlPlane.Clients.SotwAdsClient(node, ResourceTypeUrls.Route);
await client.InitializeAsync(callInvoker, new Grpc.Core.CallOptions(cancellationToken: stoppingToken));
var resources = await client.FetchAsync(stoppingToken);
// After the application accepts these resources:
await client.AckAsync(stoppingToken);
// Alternatively, reject with client.NackAsync(message, stoppingToken).
```

ACK advances accepted state before its send; NACK keeps that version and uses the latest received nonce. Explicit reinitialization retains received state and ACKs it, including a previously rejected response. Open failure retains the old call; initial-send failure retains the new call. Async operations serialize, with cancelable queueing; canceling a read may cancel the gRPC call. Use `IsConnectionError` for EOF/Unavailable/Cancelled, then choose recovery in the application. Local queue cancellation is not classified as connection loss.

Dispose cancels/releases owned calls, including the old call during replacement, and rejects further use. The caller still awaits pending operations and retains ownership of the external invoker/channel and stable node/resources. `docs/plan/T19-SOTW-ADS-CLIENT.md` records exact source semantics, runtime adaptations and scripted/loopback evidence.

## Protobuf object conversion and conventional names

`MessageConversions.ToStruct(message, typeRegistry)` uses standard protobuf JSON with proto field names. `ToMessage<T>(value, typeRegistry)` and `ToMessage(value, descriptor, typeRegistry)` return new messages; they do not merge into an existing destination. Register descriptors for opaque Any payloads explicitly. Null inputs, unknown/malformed payloads and descriptors without a CLR parser fail. Duration/ListValue scalar/array roots cannot become Struct objects, matching the source helper's boundary. The existing example listener graph can round-trip with Listener, HCM and router descriptors registered.

`WellKnownNames` supplies the complete conventional-name inventory at the behavior pin. The upstream catalog is deprecated: protobuf type URLs determine extension identity. Case-only Go aliases map to canonical C# casing; the existing two `WellKnownFilterNames` fields remain aliases. `ResourceFetchPaths` exposes the eight standard HTTP discovery paths used by the hosting gateway. See `docs/plan/T20-HELPER-COMPATIBILITY.md` for the complete symbol map, adaptations and proof.
