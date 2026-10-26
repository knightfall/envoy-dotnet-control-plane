# T16 MuxCache

`MuxCache` selects a child cache using an application classifier and directly delegates the call. SotW and Delta have separate classifiers; Fetch uses the SotW classifier. It implements the existing channel-based `IConfigWatcher` contract and the new, separate `IConfigFetcher` capability. Applications can register it as their ordinary `IConfigWatcher` without changing the stream engines or hosting adapters.

## Routing and ownership

The constructor takes an ordinal child index and the two classifiers. It copies the index, retains caller-owned child instances and exposes a fixed routing table. Classifiers must support concurrent callers. Unlike Go's publicly mutable map, the .NET table is fixed after construction; dynamic child registration is not part of this API.

Every watch receives the original request, subscription and channel writer. Its cancellation registration is returned directly, preserving `IAsyncDisposable` and the child's one-shot ownership. Missing keys throw `KeyNotFoundException` identifying the classification key. Classifier and child failures propagate unchanged. The mux adds no lock, publication queue, disposal policy or aggregate snapshot transaction; independent child caches keep their own publication coordination.

`IConfigFetcher.FetchAsync(request, ResponseContext)` returns `Task<ISotwResponse>`, retaining the existing request/cancellation/caller-state vocabulary. The mux returns the child's task directly. A selected child without this capability returns an explicit unsupported-operation failure. LinearCache implements the capability through its existing unsupported Fetch operation and retains its token-based public convenience method.

This is Fetch **delegation**, not the shared Fetch engine, SnapshotCache polling implementation, HTTP gateway or gRPC Fetch adapters. Those remain T18. SnapshotCache remains a watcher-only child until then, and Fetch through that route explicitly reports unsupported. Custom children can provide real Fetch responses now. No default successful response or fabricated unchanged result is supplied.

## Source and evidence

The behavior reference is [`pkg/cache/v3/mux.go`](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/mux.go) at `67c5345367582741334e34ffc66a561b0ce32946`, SHA-256 `bd92571c4458d86772eb76e942fa948579b5b48d77a59937d57040c03548e0cb`. The C# adaptation retains Copyright 2020 Envoyproxy Authors and Apache-2.0 attribution. The existing original license is retained and included in the Core package.

No MuxCache-specific test file/reference was found in the pinned root module's test inventory. The comparison driver therefore calls the actual Go `MuxCache`, SnapshotCache, LinearCache and subscription types, reusing existing message/response fixtures. Its custom Fetch child supplies a response or error; it contains no routing or cache algorithm. Twelve shared cases compare classifier calls, selected/unselected watch counts, cancellation, wire responses, collection/resource versions, returned metadata, missing routes and child/Linear Fetch errors. Error labels normalize the known .NET/Go exception/error representations; unexpected errors fail. Resource order is normalized because source map iteration is unspecified. Watch counts are observed before final fixture disposal.

Native requirement mapping in `MuxCacheTests`:

| Requirement | Test |
|---|---|
| Separate classifiers and exact request/subscription/writer/registration delegation (V20) | `Classifiers_DelegateOriginalInputsAndRegistration` — SotW/Delta |
| Fetch shares SotW classifier, preserves request/context and returns the child's task/response | `Fetch_DelegatesOriginalContextTaskAndResponse` |
| Missing classification keys remain visible in all three modes | `MissingRoutes_ExposeTheClassificationKey` |
| Classifier and synchronous child failures retain their original exception | `ClassifierAndChildFailures_ArePreserved` — both origins in all modes |
| Asynchronous Fetch failure is not wrapped | `FetchTaskFailure_IsReturnedWithoutWrapping` |
| Linear and watcher-only Fetch limitations stay visible | `UnsupportedFetch_RemainsVisibleForLinearAndWatcherOnlyChildren` |
| Fixed ordinal routing index and caller-owned children | `RoutingIndex_IsCopiedAndChildrenRemainCallerOwned` |
| Real mixed children, both modes and cancellation limited to the selected registration | `MixedChildren_RouteBothModesAndCancelOnlyTheSelectedWatch` |
| Progress through another child while one publication is backpressured | `BlockedChild_DoesNotPreventOtherRoutesFromProgressing` |
| Declared outcomes and actual pinned Go comparison | `SourceCases_MatchDeclaredOutcomesAndExportActualObservations` — twelve source cases |

Nineteen new native cases bring Core to **211 tests** and the solution to **355 tests**. Focused native tests, the full Core suite and all twelve actual Go comparisons passed locally on macOS arm64. Complete local `repository verify` also passed all 355 solution cases, all Go corpora, five package/symbol pairs, fresh Protocol 14 / Validation 18 / Core 211 / Hosting 53 test consumers and both executable package-graph consumers. Compiler/Roslynator warnings and errors were zero; the three existing non-packable-project notices remain. An altered classifier observation was rejected by the Go comparer. Exact-head CI/review results are recorded separately on the implementing PR. These cache/package observations do not claim live-proxy or performance acceptance.

After binding generation, `dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify` runs all suites, all Go corpora, packing and fresh consumers. It retains Mux results under Protocol's `obj/upstream/results/mux-cache/go-results.json` in CI artifacts. Existing pins, generated/descriptor fingerprints, optional validation separation and the manual-only performance baseline remain in place.
