# Upstream test reuse

The behavior reference is [envoyproxy/go-control-plane at `67c5345367582741334e34ffc66a561b0ce32946`](https://github.com/envoyproxy/go-control-plane/tree/67c5345367582741334e34ffc66a561b0ce32946). Its tests and reused code are Apache-2.0, Copyright 2018 Envoyproxy Authors. The original [license text](licenses/go-control-plane/LICENSE) is retained here. The source archive/license hashes are also recorded by the repository's existing upstream lock.

## Code used directly

The Go comparison imports the actual upstream SotW `NewServer`/`StreamHandler`, subscription implementation and `cache.PassthroughResponse`. The existing [module manifest](GoOracle/go.mod) and [checksums](GoOracle/go.sum) retrieve them at the immutable behavior revision. The repository CLI verifies both root and Envoy module revisions and rejects replacements before comparing results. No server or subscription algorithm is rewritten in the comparison.

Go modules already provide the required source retrieval and version ownership. A Git submodule would add another checkout and pin for the same source; this test arrangement uses the existing module mechanism. .NET package consumers need neither mechanism.

## Adapted fixture and scenarios

[GoOracle/stream_fixture.go](GoOracle/stream_fixture.go) adapts `mockStream` from [upstream `pkg/server/v3/server_test.go`](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/v3/server_test.go). The original file's SHA-256 is `e9f2a9662662f5ebd4b361fffc842e4c646a43df47ade515a3e563aa82e7256c`. The file retains the original license header and a modification notice. Changes replace `testing.T` assertions with observation recording, use explicit channel handoffs, expose receive errors and make blocked input/output cooperate with cancellation. Expected observations in the shared corpus perform the assertions.

Selected C# test scenarios are ports of the same source tests, retaining their copyright/license header and marking the adaptation:

| Upstream scenario | C# adaptation |
|---|---|
| `TestResponseHandlers` | `DefaultServer_DeliversWithoutCallbacksAndReleasesItsWatch` |
| `TestStaleNonce` | `StaleRequest_IsCallbackVisibleAndCapturesNodeWithoutReplacingWatch` |
| `TestAggregateRequestType` | `MissingAdsType_RejectsBeforeRequestCallbackAndStillCloses` |
| `TestServerShutdown`, `TestCancellations` | `InputTermination_ReleasesWatchesAndClosesOnce` |
| `TestSendError` | `OperationFailure_PreservesOriginalAndCleansUp` — send case |
| `TestCallbackError` | `OperationFailure_PreservesOriginalAndCleansUp` — open case |
| `TestNilPropagationOverResponseChannelShouldCloseTheStream` | `MissingStreamValue_ReturnsUnavailable` — response case |
| `TestAggregatedHandlers` | `SharedQueue_SendsProducerOrderAcrossTypes`; `ReadinessAndOrdering_AreIndependentOptions` |
| `TestAggregatedHandlersDropStaleResponseAfterUnsubscribe` | `LateUnsubscribedResponse_IsFilteredAgainstReplacementSubscription` |

The ports use Core's asynchronous input/writer API and xUnit. They replace Go's `testing.T`/testify calls with xUnit assertions, use explicit handoffs instead of timing assumptions, and add the .NET lifecycle observations documented in T09. Other cases in these files are repository-specific additions, not claims of verbatim upstream tests.

T10's ordered loop follows `pkg/server/sotw/v3/ads.go` at the same revision (SHA-256 `eb07627abe1d271d36bf070bc08c6c1c039e022e3c2b166f19afcc85b01d01e3`), retaining its copyright/license notice. The adaptation uses standard .NET channels, asynchronous publication and owned asynchronous disposal instead of blocking writes under cache locks. Sixteen shared traces now include six ordered variants, explicitly gated producer-order/drain cases, and response-then-channel-closure cases in both modes. `WithOrderedADS` runs the actual upstream ordered handler; the adapter does not reproduce its algorithm. Both named upstream ADS test functions were also run unchanged in the pinned source: two passed, zero failed/skipped; those reference-only results are distinct from C# comparison evidence.

## Remaining comparison code

`streams.go` reads the shared trace actions and records callbacks, watch creation, sends and disposal counts. The small controlled watcher exposes each response channel so the same actions can be played against Go and C#. The upstream `mockConfigWatcher` discards subscription metadata; it cannot supply those comparison observations unchanged. The C# input/watch fixtures bridge the .NET interfaces and inject the documented errors. These adapters support the comparison; they are not alternate implementations of xDS.

The original upstream test functions remain available unchanged through `go test`; the completed investigation records nine selected functions and 49 passing leaf results. Running those tests alone verifies the reference. The maintained shared traces additionally compare that reference with the C# engine. Actual network/Envoy acceptance remains a separate gate.

## Preview resource helpers

T20 preview reuses the actual pinned `resource.GetHTTPConnectionManager`, cache snapshot/reference helpers and the two `wellknown` labels. The labels retain their source copyright notice in `WellKnownFilterNames.cs`; the existing upstream license is packaged with Core. The upstream well-known package is deprecated, so this preview deliberately exposes only the HCM/router labels used by its example. `GoOracle/helpers.go` decodes the shared inputs and compares eight independently declared Any outcomes plus the exact example graph; it does not reproduce the helper algorithms. The existing snapshot wire decoder is shared with this driver. The CLI verifies declared case membership and both root/Envoy module pins before comparison. See `docs/plan/T20-PREVIEW-HELPERS.md` for symbol mapping and deferred full-T20 scope.

## Preview differential completion

T21 retains the same immutable module, adapted stream fixture and license. Six additional shared stream scripts exercise rejected open, rejected request after an existing watch, and missing ADS type before request callback in both modes. Go uses its ordinary gRPC status error; C# uses the existing equivalent protocol classification. The driver exports actual Go observations for the C# comparer. No server algorithm or new runtime policy is added. A 23rd cache script re-observes the actual retained response after replacement publication. See [T21 evidence](../../docs/plan/T21-PREVIEW-DIFFERENTIAL.md) for the exact preview requirement mapping, nonce associations and injected mismatch checks.

## Released Envoy bootstrap

[T23](../../docs/plan/T23-PREVIEW-ENVOY.md) adapts `sample/bootstrap-ads.yaml` from the same immutable Go reference commit; its Git blob identity is `1acb2c58883d27288ce5931db245a6e0af111630`. `envoy/bootstrap-ads.yaml` retains the upstream copyright/license and a modification notice: the unused ALS cluster is omitted and node/loopback ports are substituted by the probe. The original Apache-2.0 license remains under `licenses/go-control-plane/`. The C# probe reuses the actual example resource graph and generated admin contracts; its process/HTTP observations are necessary .NET integration glue, not an alternate xDS implementation. Exact official Envoy/SDK image digests, version and source commit are separate from the Go/schema pins in `envoy-images.lock.json`.

## Delta subscription state

T13's twelve expected traces derive from `TestDeltaSubscriptions`, `TestDeltaSubscriptionsWithDeactivatedLegacyWildcard` and the prefix branches in the same pinned `pkg/server/stream/v3/subscription.go`. The source/test SHA-256 values and precise state boundary are recorded in [T13 evidence](../../docs/plan/T13-DELTA-SUBSCRIPTIONS.md). Expected JSON states replace Go assertion helpers; two focused C# cases add read-only ownership and independent initialization proof. `GoOracle/delta_subscription.go` invokes the actual upstream Delta constructor/update/returned-map APIs and reuses `observeSubscription`; it contains no subscription algorithm. Existing Go modules/checksums and the retained Apache-2.0 license remain authoritative. T14 must separately prove server first-request routing and Delta responses.

## Delta cache foundation

T14.1 adapts the behavior exercised by `pkg/cache/v3/delta_test.go`, including `TestSnapshotCacheDeltaWatch`, `TestDeltaRemoveResources`, cancellation and update cases. Source SHA-256: `simple.go` = `3709788d7edbfa47369513a93ef355cbb2b6fa8074d7e69074f385033ca7eb6a`; `cache.go` = `3b25b56c1adcb240afe1d070ddf14a93525167bba57e4d3210bd291560e3a866`; `delta_test.go` = `b37e7679f2f6678c4f59cb2fd020af5258993451edea8c0921ef23e310346290`. The original files are at the same immutable behavior commit and Apache-2.0 license above. C# response/selection adaptations retain attribution. `GoOracle/delta_cache.go` builds declared RouteConfiguration fixtures and calls actual SnapshotCache/Delta APIs; it does not copy selection logic. Fourteen cases compare actual payload/version/removal metadata, status and lazy maps, including the pinned prefix/TTL limitations. Native cancellation/ownership cases exercise the necessary .NET asynchronous adaptation. [T14 foundation evidence](../../docs/plan/T14-DELTA-CACHE.md) separates these results from future stream and network proof.

## Delta stream processing

T14.2/T14.3 import the actual pinned `delta.NewServer`/`DeltaStreamHandler`, `cache.DeltaPassthroughResponse` and source option functions. The fixture in `GoOracle/delta_streams.go` adapts `mockDeltaStream` from `pkg/server/v3/delta_test.go` (SHA-256 `16ed02e47cee42c3ed7fa882c52fbd0e6ed5a06b76832b7ecc0fad6a1eaf2061`). It retains attribution and replaces test assertions with recorded observations and cancellation-aware handoffs. The same original Apache-2.0 license remains authoritative. Ten expected traces cover callback normalization/mutation, first versions per type, old nonce/NACK updates, shared queue order, legacy options, callback nonce replacement, rejection and receive errors. C# native cases adapt the corresponding response/send-error/callback-error/ADS/cancellation behavior to the existing asynchronous APIs; additional disposal/ownership assertions are specific to .NET. [Delta stream evidence](../../docs/plan/T14-DELTA-STREAMS.md) records the server/watch source hashes and exact test mapping. No stream or subscription algorithm is copied into the comparison driver; no network or released-Envoy Delta acceptance is claimed.

## LinearCache

T15 adapts scenarios from the pinned `pkg/cache/v3/linear_test.go`: initial resources, corner cases, basic/set/get/version-prefix/deletion/watch-two/cancel behavior, Delta wildcard/existing/initial-version/update/delete/bulk cases, mixed watches, SotW full/partial selection and prefix subscriptions. Source SHA-256 values and the exact native mapping are in [T15 evidence](../../docs/plan/T15-LINEAR-CACHE.md). The implementation and `GoOracle/linear_cache.go` retain Copyright 2020 Envoyproxy Authors and Apache-2.0 attribution; the original license remains under `licenses/go-control-plane/` and is already included in the Core package.

Thirty-one shared fixtures replace Go assertion helpers with declared expectations and recorded observations. The driver imports actual `NewLinearCache`, mutation/watch APIs and subscription types using the existing immutable module/checksum pins; no selection algorithm or upstream source fork is added. Native cases cover the .NET channel and stable-message ownership adaptation. Idempotent disposal intentionally avoids the source's possible negative diagnostic counter after cancellation of an already delivered watch. Counts otherwise compare exactly, including index overlaps.

## MuxCache

T16 adapts `pkg/cache/v3/mux.go` at the same immutable behavior commit (SHA-256 `bd92571c4458d86772eb76e942fa948579b5b48d77a59937d57040c03548e0cb`), retaining Copyright 2020 Envoyproxy Authors and Apache-2.0 attribution. The original license already travels with Core. No MuxCache-specific reference was found in the pinned root module's tests, so `GoOracle/mux_cache.go` directly imports the actual MuxCache and existing Snapshot/Linear/subscription implementations, reusing the repository's existing message and response fixtures. Its custom Fetch child is necessary comparison glue; no mux or cache algorithm is copied into it. Twelve declared expected cases and nineteen native cases establish the bounded T16 behavior in [T16 evidence](../../docs/plan/T16-MUX-CACHE.md), including .NET ownership/interface choices. SnapshotCache polling and Fetch transport acceptance remain T18.

## TTL heartbeats

T17 adapts `simple.go`'s `NewSnapshotCacheWithHeartbeating`/`sendHeartbeats` and `cache.go`'s TTL materializer at the same behavior pin, retaining Copyright 2018 Envoyproxy Authors and Apache-2.0 attribution. Source hashes and precise boundaries are in [T17 evidence](../../docs/plan/T17-TTL-HEARTBEATS.md). `GoOracle/heartbeats.go` adapts `TestSnapshotCacheWithTTL` observations by calling the actual heartbeat cache with existing fixtures and a sentinel/scan barrier, replacing broad timed sampling with exact observations. No scheduler or selection algorithm is rewritten in the driver. Native tests use Microsoft's fake-time package plus explicit channel gates for the necessary .NET asynchronous ownership adaptation. The original license is already retained and packaged. Raw Delta TTL and automatic Delta heartbeat limitations remain source-matched.
