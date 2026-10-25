# T08 — SotW subscription transitions

Implemented 25 October 2026 after T07 merged in PR #41 (`8f8cfaed92a6108c7aa297ee0e971d4766e00661`). Behavior baseline: `67c5345367582741334e34ffc66a561b0ce32946`. This task supplies subscription transitions, legacy-wildcard settings and the current-subscription response predicate. The stream loop that invokes the predicate remains T09/T10.

## Source and implementation

The reviewed source is [subscription.go](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/stream/v3/subscription.go), its [tests](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/stream/v3/subscription_test.go), [legacy options](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/config/config.go) and [responseMatchesCurrentSubscription](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/server.go). Source attribution and the upstream Apache-2.0 license remain in Core's package evidence.

Two files implement the task: [SotwSubscription](../../src/Envoy.ControlPlane/Streams/SotwSubscription.cs) and [SotwOptions](../../src/Envoy.ControlPlane/Streams/SotwOptions.cs). Subscription state consists of wildcard flags, exact names, prefix keys and returned versions, using the existing `ISubscription` contract. It introduces no enum state machine, ACK ledger, dispatcher, locking, version policy or package dependency.

Initial empty/null requests use legacy wildcard by default, including later empty requests while still in legacy mode. The first nonempty request permanently ends that fallback. An explicit `*` controls wildcard independently; after explicit subscriptions, empty means no resources until `*` is explicitly requested again.

Only a trailing `/*` creates a prefix subscription, retaining the separator and dropping the final star. Other names remain exact, including empty names and literal stars in other positions. Duplicate exact/prefix entries collapse. Matching is ordinal. Global legacy deactivation overrides the per-type set; arbitrary type URLs use the same exact comparison. Explicit `*` works even when legacy behavior is disabled.

`SetResourceSubscription` replaces the whole set and removes returned versions no longer covered by exact names or prefixes, except while explicit wildcard remains active. The legacy empty-request shortcut retains returned versions. This cleanup enables re-subscription at the same snapshot version. Returned metadata is supplied explicitly through `SetReturnedResources`; the class does not infer ACK/NACK state.

`MatchesResponse` accepts wildcard and empty returned metadata. Otherwise every returned name must be exact-subscribed or prefix-matched; one obsolete name rejects the response. It does not materialize payloads or inspect nonce/version values. Calling it at the source-equivalent stream point remains later integration work.

## Ownership adaptation

Subscription state belongs to the stream processing loop. Cancel or complete its current watch before mutation; caches treat the view as stable for that watch lifetime. There is no immutable-view framework or thread-safety claim for concurrent subscription updates.

Go's setter retains a mutable version map. The existing .NET response contract exposes `IReadOnlyDictionary`, so `SetReturnedResources` makes an owned mutable dictionary for cleanup. Native tests use a read-only wrapper and verify that cleanup changes the subscription without modifying the supplied metadata. The differential corpus compares state values; it does not claim identical mutable-map aliasing. This copies version bookkeeping, not protobuf messages or resources.

## Automated verification

Five native test methods expand to **12 cases**. One method checks all declared states of **14 independent transition traces** and exports them to the real pinned Go implementation. Another uses the actual T07 SnapshotCache to unsubscribe and re-subscribe without changing the snapshot version.

| Requirement | Evidence |
|---|---|
| V11 legacy empty/null, explicit star/name, prefix, duplicate sets and returned cleanup | `SourceDerivedTransitions_MatchEveryDeclaredState` checks every state in `sotw-subscription-cases.json` |
| V11 global/per-type default behavior and custom URL matching | Declared transition traces plus `LegacyOptions_UseOrdinalTypeUrlsEvenWithCallerSetComparer` |
| V10/V11 same-version re-subscription | `UnsubscribeAndResubscribe_ReturnsResourceWithoutSnapshotVersionChange` checks real cache registration, response and returned version |
| Obsolete-response predicate | Eight `ResponseMatching_RequiresEveryReturnedNameToRemainSubscribed` cases cover empty metadata, exact names, wildcard, prefixes, overlap and one stale name; payload materialization count remains zero |
| .NET read-only metadata adaptation | `ReturnedMetadata_UsesOwnedBookkeepingWithReadOnlyInputs` |

The Go `subscription` command invokes actual `config.NewOpts`, deactivation functions, `stream.NewSotwSubscription`, `SetResourceSubscription` and `SetReturnedResources`. Every observation compares wildcard mode, ordinal exact/prefix sets and returned-version values. The existing module pins are checked by repository verification before invoking this command. No reference implementation is reimplemented in the oracle. The source response predicate is private, so its native tests are source-derived; actual Go/.NET stream suppression is not claimed here.

Static review considered accidental legacy reactivation, treating prefixes as exact names, clearing legacy ACK metadata, failing returned-version cleanup, ignoring global/per-type settings, caller-comparer leakage and accepting a mixed current/stale response. The corresponding assertions distinguish these behaviors. This was a source/assertion review, not an executed mutation campaign or coverage percentage. No race tests, sleeps or timing framework are introduced for processing-loop-owned state.

## Observed local result

On macOS arm64 with .NET SDK **10.0.400**, Go **1.26.4** and unchanged Buf **1.75.0** inputs:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify
```

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Complete solution | 135 | 0 | 0 |
| Core native cases, included above | 81 | 0 | 0 |
| Independent Go subscription traces | 14 | 0 | 0 |
| Existing Go cache traces | 22 | 0 | 0 |
| Existing Go snapshot cases | 19 | 0 | 0 |
| Existing Go PGV cases | 33 | 0 | 0 |
| Fresh Protocol package suite | 12 | 0 | 0 |
| Fresh Validation package suite | 18 | 0 | 0 |
| Fresh Core package suite | 81 | 0 | 0 |

Full build: **0 warnings and 0 errors**. All five candidate package/symbol pairs and executable Core/ASP.NET consumers passed. The Core package suite stages all new source/support files and the embedded transition corpus. No schema, generator pin, evaluator source, project dependency or runtime target changed. Candidate packaging does not publish to NuGet.org. Linux/macOS/Windows CI runs the same generation and repository commands; remote observations belong to the implementing PR.

Nonce handling, request/callback ordering, node restoration, actual queued-response suppression, service adapters, ordered ADS and live Envoy interoperability remain later tasks. This closes the transition-table portion of V10/V11 rather than the full streaming gate.
