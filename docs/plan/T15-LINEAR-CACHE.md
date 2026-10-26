# T15 LinearCache

`LinearCache` manages one arbitrary type URL across nodes. Callers supply resource names; message fields do not determine the index. It implements the channel-based `IConfigWatcher` contract used by the existing SotW/Delta stream engines. Mux routing remains T16; released-Envoy Delta acceptance remains a separate T23 gate.

## Collection and versions

The constructor copies the initial index and retains stable messages at version `versionPrefix + "0"`. `UpdateResourceAsync`, `DeleteResourceAsync`, `UpdateResourcesAsync` and `SetResourcesAsync` serialize publications and increment a collection counter. A missing deletion, empty batch or empty replacement still increments it. Bulk deletions run after updates, so deletion wins for a repeated name. Single null updates fail before mutation; bulk/initial null entries preserve the source's empty-payload behavior. Replacement treats every supplied entry as changed.

SotW metadata records each resource's last update counter. Delta metadata records its payload hash. An identical update or replacement can therefore wake SotW while leaving Delta pending. Each retained entry lazily serializes once and shares immutable bytes across responses and both modes; replacement creates a fresh lazy entry. Serialization failures are cached until replacement. This preserves source sharing without changing SnapshotCache or adding a generic cache framework.

Counter identity is local to a cache instance. Applications can use a version prefix to distinguish instances. SotW creation forces a response for an empty request version or prefix mismatch; matching prefixes are not parsed as numbers. Delta creation forces an empty response only for a wildcard subscription with an empty response nonce.

## Selection and ownership

Exact names, prefixes and wildcard subscriptions have separate indexes. An update gathers matching registrations, deduplicates overlaps and computes each response once. CDS/LDS SotW responses include all currently matched resources; other SotW types return changes and suppress later removal-only responses. Delta returns changes and removals, including names no longer present or subscribed. Returned version maps are copied and subscriptions remain stream-owned.

`NumResources`, `NumCacheWatches`, `NumWildcardWatches`, `NumPrefixWatches` and `NumWatches(name)` expose source-relevant diagnostics. `NumWatches(name)` sums index entries, so overlapping exact/prefix matches can count a registration more than once. Delivery remains one-shot. `GetResources()` returns a fresh outer dictionary retaining stable message references. This cache has no node-specific status or application clock.

Channel delivery waits outside cache synchronization. Publication cancellation can leave an installed update and pending watch; a later eligible update retries it. Cancellation while waiting for another publication prevents installation. Synchronous disposal removes the registration and cancels its pending write; asynchronous disposal also settles that write before subscription mutation. Releasing a watch leaves the caller's shared writer open.

The pinned Go cancellation callback decrements its total counter even after delivery already removed the watch: an actual probe observed `1 -> 0 -> -1` after registration, delivery and cancellation. The .NET registration retains the established idempotent disposal contract and stays at zero. This is an intentional diagnostic difference.

`FetchAsync` returns a faulted task with `NotSupportedException` for every request, matching the source's unsupported operation. Shared Fetch processing and HTTP adapters remain T18. No TTL scheduling, cache-side deletion policy, dependency or performance gate is added.

## Source and verification

Behavior reference: [`67c5345367582741334e34ffc66a561b0ce32946`](https://github.com/envoyproxy/go-control-plane/tree/67c5345367582741334e34ffc66a561b0ce32946), retrieved by the existing pinned Go module. SHA-256 values:

| Source | SHA-256 |
|---|---|
| `pkg/cache/v3/linear.go` | `3e54b6771d97a75accd42b459c59b9cdeeb0ceee09a307112abc76401278fd49` |
| `pkg/cache/v3/linear_test.go` | `392a9f37edf3d0f11cc25b538fb06b9d72644330e34a7bc52ba506e63f676c8c` |
| `pkg/cache/v3/cached_resource.go` | `5e245ad5a26edc3f66f810919e3c8c6b4f50f282fe3e1a2972a5c70ad9a6e557` |
| `pkg/cache/v3/status.go` | `c2b9f4f21c00e7397a3d1a25917ca29524ef7a61a9875afb8dc500aefef40694` |

Attribution and the retained Apache-2.0 license are recorded in [upstream test reuse](../../tests/resources/UPSTREAM-TESTS.md). The Go driver calls actual `NewLinearCache` and public operations; it contains no cache/selection implementation. Thirty-one shared cases compare readiness, wire payloads, collection versions, resource hashes, removals, returned metadata, resource names and index counts. Resource order is normalized because upstream map iteration is unspecified. Counts are observed before cancellation, so the disposal difference is not hidden by a relaxed comparison.

Native tests are in `LinearCacheTests`:

| Requirement | Evidence |
|---|---|
| Initial resources, single/bulk/delete/replace, no-ops and version prefixes (V19) | `SourceCases_MatchDeclaredOutcomesAndExportActualObservations` and all 31 actual Go cases |
| Caller names and stable outer ownership | `InitialIndexAndResponses_RetainMessagesButOwnTheirIndexesAndCallerNames` |
| Mixed modes, overlapping indexes, one-shot delivery and idempotent release | `MixedModesAndOverlappingIndexes_DeliverOnceAndDisposeIdempotently` |
| Shared lazy bytes/hashes and replacement invalidation | `SerializationAndHashes_AreLazySharedAndInvalidatedOnlyForReplacedEntries` |
| Blocked delivery cancellation and retry (V30) | `BlockedPublication_CancellationRetainsTheUpdateAndWatchForRetry` |
| Blocked-write disposal and cancellation before queued installation (V30) | `DisposalSettlesBlockedWrites_AndQueuedCancellationDoesNotInstallAnotherUpdate` |
| Pending cancellation removes every index without affecting another watch | `CancelPendingRegistration_RemovesItsIndexesAndPreservesOtherWatches` |
| Both full-state SotW types and final empty deletion responses | `FullStateTypes_IncludeUnchangedResourcesAndSendTheFinalRemoval` — CDS/LDS theory cases |
| Wrong request types, invalid single updates and unsupported Fetch | `InvalidSingleUpdateAndWrongWatchTypes_DoNotChangeTheCollection_FetchIsUnsupported` |
| Custom resource errors, cached failures and recovery | `SerializationFailure_IsCachedUntilReplacement_AndDeltaCreationReportsItSynchronously` |

Eleven new cases bring Core to **192 tests** and solution discovery to **336 tests**. Local macOS arm64 Core execution and the 31-case actual Go comparison passed. Complete local repository verification also passed: all 336 solution tests, all Go corpora, five package/symbol pairs, fresh Protocol 14 / Validation 18 / Core 192 / Hosting 53 test consumers and both executable package-graph consumers. The build had zero compiler/Roslynator warnings or errors; packaging emitted the three existing non-packable-project notices. Implementing-PR CI and review results are recorded separately on the PR.

After binding generation, run `dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify`. It runs all suites, actual Go corpora, packing and fresh package consumers, including the entire Core suite. The LinearCache Go report is retained under Protocol's `obj/upstream/results/linear-cache/go-results.json` in CI artifacts. This proves cache behavior and package consumption; proxy and performance acceptance remain separate.
