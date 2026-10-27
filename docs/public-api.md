# Public API and lifetime contracts

This is the root 1.0 API review for T25. The [compatibility report](compatibility/root-1.0.md) maps behavior to evidence; the package readmes contain detailed examples. Candidate identities and release verification remain tracked in the [release checklist](releases/root-1.0.md).

## Package boundaries and versions

Protocol owns generated messages, gRPC contracts and original PGV definitions. Validation.Annotations owns shared derived annotation definitions without an evaluator. Core references Protocol and supplies transport-independent behavior. ASP.NET Core references Core and provides hosting. Only the separate experimental Validation project adds the source-built evaluator and CEL. Core-only consumption must retain that separation.

Every maintained library targets `net10.0`; the SDK is pinned in `global.json`. Older .NET targets, trimming and Native AOT have no acceptance proof. Generated protobuf full names and wire fields remain upstream identities. The three documented C# generator corrections in [BINDINGS.md](../schemas/BINDINGS.md) change colliding CLR member names only. Do not load overlapping generated binding packages.

The four root candidate packages use `1.0.0-rc.1` and the owner-selected `Knightfall.Envoy.ControlPlane` prefix: Core uses the root ID, with `.Protocol`, `.Validation.Annotations` and `.AspNetCore` siblings. Namespaces and assembly names retain their existing `Envoy.ControlPlane` identities. Clean consumers verify both the new package graph and the retained assembly identities. `Knightfall.Envoy.ControlPlane.Validation` keeps experimental version `0.1.0-t03` and remains outside this candidate. No stable 1.0 package has been published by this work. Before a final release, review source/schema updates against both the public C# API and wire/behavior contracts; record any breaking change in the release notes rather than assuming an upstream update is compatible.

## Retained data and publication

| Surface | Caller contract |
|---|---|
| `ResourceEntry`, `ResourceCollection`, `Snapshot` | Retained messages and indexes stay stable. Create replacements for updates. No deep cloning or mutation isolation is promised. Duplicate resource names select the last entry. |
| `IResourceSnapshot` | Support stable resource/version reads, nullable absent groups, explicit Delta version-map construction and arbitrary application type URLs. `Snapshot` itself supports the twelve selected known families. |
| `SnapshotCache.SetSnapshotAsync` | Retain the snapshot and await eligible enqueueing. No automatic consistency/semantic validation, ACK wait or cross-type transaction. Cancellation after installation can leave the new snapshot retained. A canceled queued wait can finish before installation. |
| Snapshot versions | Caller-supplied opaque per-type strings. Delta versions derive from deterministic protobuf payloads. TTL/type-version changes alone do not change resource hashes; cross-language hash bytes are not guaranteed. |
| `EnsureConsistent` | Explicit selected EDS/RDS reference-set checks, including scoped routes and HCM references. No universal semantic validation or Envoy acceptance guarantee. |
| `GetStatusInfo`, `ClearSnapshot` | Synchronized live node/watch/request-time status. Clearing the index does not complete existing watches. Consumers still dispose them. |
| `LinearCache` | One arbitrary type URL, caller-supplied names and optional version prefix. Keep publication input collections stable until completion and retained messages stable until replacement. Update counters follow source behavior, including no-op updates. |
| `MuxCache` | Copies the ordinal child index, uses separate SotW/Delta classifiers and shares the SotW classifier with Fetch. Classifiers support concurrent calls. Children remain caller-owned; the mux adds no cross-cache publication lock or transaction. |

SnapshotCache and LinearCache serialize publications while allowing channel-capacity waits outside cache synchronization. Buffering waits; it does not silently drop responses or promise fairness/throughput quotas. Lazy responses may fail when materialized after a successful watch delivery. `TimeSpan` carries 100 ns precision.

## Channels, cancellation and disposal

`IConfigWatcher.CreateWatch` and `CreateDeltaWatch` take a caller-owned `ChannelWriter` and return one cancellation registration. Producers establish delivery order at the writer. Releasing one registration must leave a shared writer open. If delivery owns pending asynchronous work, implement `IAsyncDisposable` and settle those writes before subscription mutation. The stream awaits that interface. Custom producers implement the writer overloads; the two-argument extensions adapt them for callers that await one result.

One-shot delivery does not transfer ownership of the shared channel or snapshot. Keep requests and subscription views stable until the watch completes or is disposed. Cancel or release the current registration before mutating its subscription. A producer may deliver immediately or throw during registration. Dispose an await-one registration to cancel a pending read, then observe completion/cancellation; delivery does not imply task continuations have already run.

Core streams own their input enumeration and watch registrations. Inputs, writers and hooks must cooperate with the combined call/host cancellation token. Cleanup releases owned work and invokes close once, including after rejected open. Host adapters propagate `ApplicationStopping`; Kestrel's graceful drain follows ordinary host settings. No forced-abort callback mechanism or extra cleanup timer is added.

`RunHeartbeatsAsync` is a caller-started SotW task using the cache's `TimeProvider`. Cancel and await it to settle in-flight writes and dispose its timer. Stopping it leaves the cache and pending watches usable. No cache expiry or automatic Delta scheduler follows from TTL encoding. See the [proxy limitation](plan/T23-ROOT-ENVOY.md) before selecting this path for a released Envoy.

## Options and extension points

| Setting / extension | Default or behavior |
|---|---|
| `SnapshotCache(ads: ...)` | False. Opt-in source readiness checks require requested exact names to cover retained resources. Readiness and stream ordering are independent. |
| `SotwOptions.OrderedAds` | False. Only SotW ADS uses the shared bounded ordered FIFO when enabled. It does not create an atomic application transaction. |
| SotW/Delta `DisableLegacyWildcard` | False. Initial empty subscriptions use the source legacy wildcard behavior by default. |
| SotW/Delta `LegacyWildcardDisabledTypes` | Empty ordinal type-URL set. Explicit wildcard subscriptions remain supported. Keep supplied option collections stable while servers use them. |
| `INodeHasher`, `TimeProvider` | Node ID routing and system clock by default; callers can replace either. Node IDs are routing data, not authorization. |
| `IResourceSnapshot`, `IConfigWatcher`, `IConfigFetcher` | Custom stable snapshots, channel producers and separate polling capability. Watch-only producers remain valid. |
| `ISotwResponse`, `IDeltaResponse` | Lazy or pass-through envelopes, originating request/context and returned-resource metadata. Follow the engine's separate SotW/Delta bookkeeping contracts. |
| `ISotwCallbacks`, `IDeltaCallbacks`, `IFetchCallbacks` | Optional asynchronous in-path hooks. Request hooks can mutate/reject, response hooks can change envelopes, close releases per-stream application state. No generic policy pipeline. |
| Protobuf `TypeRegistry` | Explicit descriptors for custom/opaque `Any` payloads in JSON/reference helpers. Hosting registry replacement must preserve required roots/wrappers. |
| `AddEnvoyControlPlane`, endpoint groups, `ILogger` | Ordinary singleton DI, gRPC/Kestrel, endpoint authorization and host logging. Application registration is retained. Supplied instances remain application-owned under normal DI rules. |

## Source-specific protocol and errors

SotW request hooks run after normalization and before stale-nonce filtering. Ordered ADS captures its dispatch type before the hook; ordinary mode uses the effective type afterward. SotW response hooks run after nonce/bookkeeping updates with the cache response's creation context, which can differ from the stream-open context.

Delta drains queued responses before request mutation and runs the request hook before node/type normalization. Returned versions advance after a successful send. Its source loop does not apply SotW stale-nonce rejection or an ACK-committed ledger. NACKs in either mode do not roll back application snapshots. Fetch hooks run request, producer, materialization and response in order; the source Fetch response hook has no context/cancellation parameter.

| Failure | Observable contract |
|---|---|
| Missing snapshot / mux key | `KeyNotFoundException`; mux delegates child errors directly. |
| Unchanged Fetch | `SkipFetchException`; HTTP gateway maps it, including ordinary wrapping, to empty 304. |
| Unsupported Fetch | `NotSupportedException` for LinearCache or a selected watch-only child. Hosting keeps streaming available. |
| Core protocol rejection | `SotwProtocolException` / `DeltaProtocolException` with protocol status; adapters map InvalidArgument/Unavailable to gRPC status. |
| Ordinary processing / callback error | Original exception retained. Ordered main-queue response failures map to Unavailable with the inner cause; drain failures preserve source-specific propagation. |
| Multiple processing / cleanup errors | Aggregate retains processing failure first and subsequent cleanup failures. Close is attempted once. |
| Application `RpcException` | Existing gRPC status retained; unexpected transport failures follow framework handling. |
| HTTP gateway | 200 protobuf JSON, empty 304 unchanged, 400 bad/read-failed JSON, 404 unknown path, 500 Fetch/materialization/format failure. Host failures may prevent any response. |

Error strings/order, JSON whitespace/property order and time precision are runtime adaptations. See the task evidence for exact tested paths. The library adds no retry policy, transport quota or authentication provider.

## Client and helpers

`SotwAdsClient` fixes one node/type, serializes operations and owns its active call. The application owns the invoker/channel and stable node. Explicit initialization sends the initial ACK; ACK advances accepted state before send, NACK retains the accepted version with the latest nonce, and reinitialization retains received state as the source does. Dispose cancels/releases the call; callers still await pending operations. Read cancellation can cancel the gRPC call. Reconnection, decoding and validation are application decisions.

Resource/reference helpers and `MessageConversions` use generated messages and stock Protobuf JSON. Conversion creates new messages, requires explicit opaque `Any` descriptors and can fail for malformed payloads or non-object Struct roots. Conventional names are compatibility labels; protobuf type URLs define extension identity. The [T20 map](plan/T20-HELPER-COMPATIBILITY.md) lists all selected symbols and necessary C# adaptations.
