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
