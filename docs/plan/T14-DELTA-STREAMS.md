# T14 Delta stream processing

This increment implements **T14.2/T14.3** in Core: `DeltaServer`, channel-based `DeltaSession`, `DeltaOptions` and `IDeltaCallbacks`. T14.1's cache/response work was merged in PR #57. Delta gRPC adapters and their real HTTP/2 matrix remain **T14.4**. The SotW preview and its released-Envoy proof do not establish Delta proxy acceptance.

## Source and behavior

Behavior reference: [go-control-plane `67c5345367582741334e34ffc66a561b0ce32946`](https://github.com/envoyproxy/go-control-plane/tree/67c5345367582741334e34ffc66a561b0ce32946). Schema, Buf/plugin and dependency pins are unchanged.

| Source | SHA-256 |
|---|---|
| `pkg/server/delta/v3/server.go` | `d6a8f18e5e17ca544beaaed2b6e80631a17e5948fba84155d7ae684938669184` |
| `pkg/server/delta/v3/watches.go` | `c2035335a65405c74d3b3082e25f94ed153c0699bef3e5bd0403654d8e6ef4c2` |
| `pkg/server/v3/delta_test.go` | `16ed02e47cee42c3ed7fa882c52fbd0e6ed5a06b76832b7ecc0fad6a1eaf2061` |

One shared bounded channel carries cache responses for ordinary per-type and ADS streams. Its capacity is **24**, twice the twelve known families, matching the source. Arbitrary type URLs use dictionary entries without inventing a type quota. The loop owns a single asynchronous input enumerator and awaits every send. Producers use the existing channel-primary watcher contract; await-one tasks remain a consumer convenience.

Before processing a request, the loop drains responses already in the shared channel. The request callback then sees the original request, before node carry and missing-type normalization. Callback mutations affect both captured node and dispatch. The first request for each type constructs a subscription from its initial versions; later requests update additions/removals without re-seeding. Replacements await asynchronous watch disposal before mutation. A missing ADS type is rejected after its callback; an implied type fills only an empty type URL.

The stream assigns monotonically increasing nonce strings across all types, then invokes the response callback and sends. Only a successful send replaces returned-version metadata. Request nonces and NACKs do not trigger SotW stale filtering, rollback or an ACK-committed version ledger. The source writes a per-watch nonce that this path never reads; Core retains only the observable stream-wide counter. There is no ordered-Delta option.

## .NET ownership and lifecycle

`StreamAsync` accepts `IAsyncEnumerable<DeltaDiscoveryRequest>`, an awaited response delegate, an implied type (empty for ADS) and `ResponseContext`. Optional asynchronous callbacks run serially outside cache locks. `DeltaOptions` controls legacy wildcard globally or per type. No new external runtime dependency is needed.

Call/host cancellation releases the input and all watches. Inputs, writers, callbacks and asynchronous disposal must cooperate. EOF/receive errors end normally. Close runs once after resource release, including rejected open/request. Application exceptions retain their identity; multiple processing/cleanup failures are aggregated with processing first. `DeltaProtocolException` classifies invalid ADS requests and unusable stream values independently of a transport.

As with SotW, each send clones the materialized envelope before changing nonce/callback metadata so custom caches may retain their envelope. Immutable payload bytes remain shared. This is an intentional .NET ownership convention; it does not change the tested wire/callback outcomes. A producer must not close a caller-owned shared response writer. Core terminates such a closed channel as unavailable instead of reproducing the source's closed-channel busy loop.

## Verification

The native suite covers the following independent outcomes. Its bounded gates prove processing stages without arbitrary sleeps or a generalized race framework.

| Requirement | Exact test |
|---|---|
| Default server composes with actual SnapshotCache | `DefaultServer_DeliversSnapshotAndReleasesItsWatch` |
| Await disposal before subscription mutation | `WatchReplacement_SettlesAsynchronousDisposalBeforeChangingSubscription` |
| Original request callback, mutation and node carry | `RequestCallbacks_RunBeforeNormalizationAndCaptureTheirNodeChanges` |
| First versions per type; later nonce/NACK updates | `EachType_SeedsVersionsOnceAndDoesNotFilterOldNoncesOrNacks` |
| Shared queue order and drain before callback/update | `BufferedResponses_DrainBeforeTheNextRequestCallbackAndSubscriptionUpdate` |
| Post-send metadata, callback nonce and retained envelope | `ReturnedVersions_AdvanceOnlyAfterSuccessfulSend` |
| Materialization/callback/send failure identity and cleanup | `DeliveryFailure_PropagatesAndClosesOwnedResources` |
| Open/request rejection and ADS validation stage | `RejectedLifecycle_ClosesOnceAndDisposesInput` |
| Global and per-type legacy settings | `LegacyWildcard_UsesGlobalAndPerTypeOptions` |
| Cancellation at a blocked writer without advancing versions | `Cancellation_ReleasesThePendingWriteAndWatch` |
| Ten declared traces and exported observations | `SourceTraces_MatchDeclaredDeltaCallbacksWatchesAndSends` |

`delta-stream-cases.json` declares ten expected traces. `GoOracle/delta_streams.go` imports the actual pinned Delta server and pass-through response; it adapts the existing upstream mock Delta stream for cancellation-aware handoffs and recording. It compares exact request/callback/watch/send events, returned versions, explicit nonce tokens and disposal counts. It contains no server/subscription algorithm. The original Apache-2.0 license and provenance remain under `tests/resources/`.

Observed locally on macOS arm64: **181 Core / 303 solution tests passed**, zero failed/skipped and zero compiler/Roslynator warnings/errors. All **10 actual Go Delta stream traces** and existing corpora passed, including the injected SotW stale-association rejection. Five package/symbol pairs and full fresh Protocol **14**, Validation **18**, Core **181**, Hosting **31** test consumers plus both executable graph consumers passed. The existing three non-packable-project notices remain. No source/tool/binding/dependency pin changed.

Exact-head CI and independent GPT-6.1 Sol xhigh review are pending. Evidence is written beneath `obj/upstream/results/delta-streams/` by `repository verify`; normal PR verification does not run the manual performance baseline. Delta adapters/HTTP2 and released-Envoy Delta acceptance remain separate, unclaimed gates.
