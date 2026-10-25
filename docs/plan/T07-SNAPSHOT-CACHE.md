# T07 — SotW SnapshotCache and status

Implemented 25 October 2026 after T06 merged in PR #40 (`878d7df9509e9fe74e8d48cf363ddf189782a4b3`). Behavior baseline: `67c5345367582741334e34ffc66a561b0ce32946`. This is the preview SotW cache slice; Delta delivery, heartbeat scheduling, stream state and network adapters remain later tasks.

## Source contract and implementation

The reference is [simple.go](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/simple.go), [status.go](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/status.go), [order.go](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/order.go), [response materialization](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/cache.go) and the corresponding [cache tests](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/simple_test.go). The Core package already includes upstream source attribution and license.

[SnapshotCache](../../src/Envoy.ControlPlane/Cache/SnapshotCache.cs) retains per-node snapshots and pending SotW watches under one synchronization gate. Snapshot lookup and registration share that gate, so publication cannot fall between them. `SetSnapshot` stores the replacement first and completes eligible watches without invoking consistency/validation or awaiting proxy ACK. `GetSnapshot` returns the retained object or throws `KeyNotFoundException`. Published snapshots and messages follow the stable-resource contract from T05.

`INodeHasher` accepts an application mapper; the default `IdNodeHasher` uses `Node.Id`, including an empty key for an absent node. `GetStatusInfo` returns a live synchronized `IStatusInfo` with the first retained node metadata, counts and request times. Publication alone creates no status. A standard application `TimeProvider` drives status timestamps, defaulting to system UTC time; no custom clock framework or additional package is introduced. Delta counts/times remain zero while Delta delivery is unavailable.

[Selection](../../src/Envoy.ControlPlane/Cache/SotwResponseSelection.cs) follows the source's version, subscription and returned-resource rules. The default [response](../../src/Envoy.ControlPlane/Cache/RawSotwResponse.cs) retains request/version/context and materializes lazily through one `Lazy<DiscoveryResponse>`. It caches normal protobuf payloads, preserves unknown fields, and encodes an optional TTL in the standard discovery-resource wrapper. Materialization failures remain caller-visible after successful watch delivery. Heartbeats, automatic expiry and Delta bodies are not implemented here.

## Behavior established by the fixtures

- No snapshot registers a pending watch. An available update can complete before `CreateWatch` returns. Same-version requests can respond when newly subscribed resources were not previously returned.
- Cluster and Listener return full requested state when required; other families can return only newly subscribed resources while retaining earlier returned versions. Publication skips a watch before selection if that type's version equals its request version, as upstream does.
- The ADS gate checks whether non-wildcard exact names cover resources **present in the snapshot**. An uncovered snapshot resource during watch creation produces a pending task without an indexed registration. Extra requested names absent from the snapshot are allowed when coverage is otherwise complete. An already registered watch remains registered while publication cannot satisfy coverage.
- The selected SnapshotCache source path does not expand subscribed prefixes. Wildcard plus overlapping explicit names can duplicate payload entries, while returned metadata remains indexed by name. The traces preserve these source outcomes rather than adding selection policy.
- `ClearSnapshot` removes snapshot and indexed status. It does not complete/cancel old watches or synthesize deletion responses. A retained old status handle can retain its former counts. Old cancellation cannot remove a newly indexed watch because IDs remain cache-global and unique.
- ADS publication evaluates known response families in source order. This does not implement or establish the ordered ADS wire-stream engine.

These observations correct earlier plan wording that described the ADS gate as a missing-requested-resource check. The paired fixtures `ads-incomplete-coverage-untracked` and `ads-extra-requested-missing-is-allowed` establish the direction using the actual pinned Go cache.

## Automated evidence

The existing [Core suite](../../tests/Envoy.ControlPlane.Tests/) gains 19 test methods expanding to **21 native cases**, including one corpus method that checks every step of **22 declared event traces**. No new test project, package dependency, schema, generated-code correction or tool pin is introduced.

| Requirement | Representative evidence |
|---|---|
| V07 stable snapshots and explicit checks | `Publication_DoesNotValidateOrCheckConsistency`, `ReplacementPublication_DoesNotChangePreviouslyDeliveredResources` |
| V08 immediate, pending, update and cancellation | `MissingSnapshot_RegistersUntilPublicationAndConsumesWatchOnce`, `ImmediateAndSameVersionPending_UseReturnedResourceMetadata`, `Cancellation_ReleasesRegistrationAndRetainsNodeStatus` |
| V08/V30 lookup and publication | `RegistrationAndPublication_RetainReplacementWhenLookupIsInProgress` uses a gated custom snapshot while another operation attempts publication |
| V09/V10 readiness and selection | All 22 `cache-cases.json` traces, including full/partial same-version responses, missing names, overlap and prefixes |
| V14 node/status/clear | `CustomHashAndClock_DriveLiveStatusAndRetainFirstNode`, `DefaultHasher_UsesNodeIdAndEmptyKeyForAbsentNode`, `Clear_RemovesIndexedStateWithoutCompletingOldWatchOrCancelingNewOne` |
| Custom snapshot and failure stages | `CustomSnapshot_CanServeAnArbitraryTypeUrl`, `CustomSnapshotFailure_IsSynchronousDuringCreationAndLeavesStatus`, `FailedPublication_StoresSnapshotAndKeepsWatchForRetry` |
| Lazy materialization/normal TTLs | `Materialization_IsLazyAndCachedWithRequestAndVersionRetained`, `NormalResponse_PreservesTtlPresenceAndUnknownPayloadFields` (absent/zero/nonzero), `SerializationFailure_StaysLazyAndVisibleAfterWatchDelivery` |
| V29/V30 producer progress | `BlockedConsumer_DoesNotHoldPublicationOrOtherNodeProgress`; explicit gates and bounded failure waits, without sleep/stress loops |
| Preview availability | `DeltaDelivery_IsExplicitlyUnavailableInTheSotwSlice`; Delta throws before creating status |

The source-to-test inventory ran once before new tests and is a static pairing result, not runtime coverage. A first failing publication test used an absent family whose version remained empty, so source semantics correctly skipped it. The fixture was corrected to supply a changed Listener version. Static review also strengthened corpus observation to reject faulted tasks instead of classifying them as pending. No runtime workaround or warning suppression was added.

The [Go oracle](../../tests/resources/README.md) now accepts a `cache` command in its existing pinned module. It reconstructs original protobuf wire messages and runs each event trace through the actual source `SnapshotCache`, using a capacity-one channel like the normal SotW stream. Every step compares snapshot presence/version, pending registration count, response names with duplicate multiplicity, opaque version, returned-resource versions and clear/cancel outcomes. The repository CLI verifies all declared trace IDs and both Go module revisions before comparison. Existing 19 snapshot and 33 PGV comparisons remain unchanged.

Static source/assertion review considered removed cleanup, first-result replacement, eager serialization, automatic validation, wrong ADS gate direction, prefix expansion, deduplicated wildcard payloads, same-version partial/full-state changes and clear-induced synthetic completion. The corresponding observations distinguish those changes. This was not an executed mutation campaign, coverage percentage or throughput test.

## Observed local result

On macOS arm64 with .NET SDK **10.0.400**, Go **1.26.4** and unchanged Buf **1.75.0** inputs:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify
```

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Complete solution, eleven maintained projects | 123 | 0 | 0 |
| Core native cases, included above | 69 | 0 | 0 |
| Independent Go cache event traces | 22 | 0 | 0 |
| Existing independent Go snapshot cases | 19 | 0 | 0 |
| Existing Go PGV cases | 33 | 0 | 0 |
| Fresh Protocol package suite | 12 | 0 | 0 |
| Fresh Validation package suite | 18 | 0 | 0 |
| Fresh Core package suite | 69 | 0 | 0 |

Full build: **0 warnings and 0 errors**. All five candidate packages and five symbol packages passed content checks; separate Core/ASP.NET executable consumers passed exact dependency/shared-framework checks. The source and package-only suites include both embedded corpora and all new support files. Linux/macOS/Windows CI runs the same generation/repository commands; remote results belong to the implementing PR. Candidate packaging does not publish to NuGet.org.

## Adaptations and remaining limits

The .NET API uses one result task per watch. The Go comparison uses capacity-one source channels; it does not claim arbitrary unbuffered Go-channel blocking or ordered ADS shared-buffer behavior. Those consumer/transport choices remain separate integration evidence. Disposal maps to consumer cancellation; the report does not claim Go closes its response channel.

An already canceled publication token is checked before delivery, after storing the snapshot, allowing retry with the watch retained. A canceled Go context and a ready buffered send can race in its select; the .NET check gives cancellation a deterministic outcome. Native tests record that adaptation, while the differential traces use uncanceled publication contexts. No automatic retry policy is added.

TTL precision remains 100 ns through `TimeSpan`; opaque versions and stable-message restrictions remain unchanged. Fetch, Delta delivery, heartbeat timers, subscription transitions, stream callbacks/nonces, hosting and live Envoy interoperability remain later tasks. This proof establishes the SotW cache slice rather than the complete control-plane release gate.
