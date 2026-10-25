# T10 — Channel delivery and ordered SotW ADS

Implements [issue #11](https://github.com/knightfall/envoy-dotnet-control-plane/issues/11) against behavior revision `67c5345367582741334e34ffc66a561b0ce32946`, using `sotw/ads.go`, `sotw/xds.go`, `cache/simple.go` and the upstream ADS scenarios. [Test attribution](../../tests/resources/UPSTREAM-TESTS.md) records the reused source, immutable module retrieval and retained license. Schema, generator, descriptor and generated-code pins are unchanged.

## Delivery and ownership

`IConfigWatcher.CreateWatch(request, subscription, ChannelWriter<ISotwResponse>)` is the SotW producer contract. Ordinary streams read an individual bounded channel per watch. `SotwOptions.OrderedAds` defaults to false and enables a shared bounded FIFO for ADS calls only. Both stream paths read channels directly. `Task` values used to await channel readiness are asynchronous operations; they do not determine publication order. The two-argument `CreateWatch` extension is an await-one adapter over an individual channel for callers outside the stream engine. Delta retains its existing task contract until T14.

The shared queue has twelve slots, matching the source's known-family count. Type state remains an ordinal dictionary; custom types can exceed twelve active registrations. Channels wait for capacity, run continuations asynchronously and have one reader and potentially several producers. There is one processing loop and one awaited network writer, with no dispatcher worker, priority queue or response-dropping policy.

`SnapshotCache.SetSnapshotAsync` replaces synchronous publication. It installs the snapshot and starts eligible writes under cache synchronization, then awaits destination capacity after releasing that synchronization. Concurrent publications are serialized so later publications cannot overtake a blocked one. A registration owning asynchronous writes implements `IAsyncDisposable`; the stream cancels and awaits it before mutating subscription state. Disposal leaves a shared destination open and prevents a blocked obsolete write from arriving after the drain. Publication failures/cancellation belong to the publisher; disposal observes delivery without duplicating those failures. A canceled publication can leave the new snapshot installed, while an undelivered watch remains available for retry, matching the source's publication semantics.

This is an unreleased producer API change: custom SotW watchers accept the channel writer and return a cancellation registration; callers await `SetSnapshotAsync`. Standalone `Watch<T>` remains available. The task convenience helper preserves awaited results and cancellation, while tests no longer assume its continuation has run at the instant of enqueueing. The source comparison observes channel availability directly. Ordinary channels remain observed after delivery until replacement or cleanup, preserving terminal closure/failure detection. The initial implementation missed that rearm; independent Astra medium review identified it, both focused closure/failure regressions failed before the fix, and the corrected loop rearms the standard channel wait.

## Ordered behavior

Cache readiness (`new SnapshotCache(ads: true)`) and stream ordering (`new SotwOptions { OrderedAds = true }`) are independent. Cache ADS mode retains exact coverage checks and starts eligible publication writes in known-resource priority order. Ordered ADS preserves the resulting enqueue order across types. Ordinary mode promises no ordering among simultaneously ready types.

For an accepted request replacing an existing type, the loop closes the old registration, drains queued responses for other types, skips queued responses for the superseded current type, updates its subscription and creates the replacement. A stale nonce remains callback-visible but does not replace the watch or run that drain. Returned-name filtering checks late responses against the current subscription before materialization. Per-type nonce state remains independent and assigned wire nonces remain stream-wide.

Mode-specific source details remain observable: ordered ADS captures the dispatch type before the request callback, while ordinary mode uses the effective type after it. Response errors selected from the ordered main queue become `Unavailable`, retaining the original .NET exception as the inner cause; failures during replacement draining retain the original exception. Cleanup still attempts every owned registration and closes once.

These behaviors do not guarantee an atomic transaction across types, proxy ACK ordering, delivery of a deliberately superseded current-type response, or a throughput/fairness SLA. They use C# channels and asynchronous ownership to match outcomes without reproducing Go's blocking synchronization.

## Requirement coverage

| Requirement | Exact test evidence |
|---|---|
| V09: independent readiness and ordering, partial then full subscription | `ReadinessAndOrdering_AreIndependentOptions` — all four combinations; existing 22-case cache corpus retains uncovered/untracked cases |
| V12: independent type state, stream nonce uniqueness and stale callbacks | `CustomTypes_ExceedKnownFamilyCountAndKeepIndependentNonceState`; `StaleRequest_DoesNotDrainOrReplaceItsWatch`; six ordered variants of existing stream traces |
| V13: producer FIFO and drain behavior | `SharedQueue_SendsProducerOrderAcrossTypes`; `Replacement_DrainsOtherTypesAndDropsSupersededCurrentType`; gated `ordered-buffered-producer-order` and `ordered-replacement-drains-other-type` Go traces |
| V13: late unsubscribe suppression in both modes | `LateUnsubscribedResponse_IsFilteredAgainstReplacementSubscription` |
| V13: cache publication priority | `AdsPublication_UsesResourcePriorityIndependentlyOfRegistrationOrder` |
| V12/V13: source callback/error differences | `RequestCallbackTypeMutation_UsesModeSpecificTypeCapture`; `SendFailure_UsesSourceMainQueueAndDrainErrorBehavior` |
| V30: bounded backpressure without holding cache synchronization, publication order | `FullDestination_AwaitsSpaceOutsideCacheLockAndSerializesPublications` |
| V30: settle canceled delivery before changing subscription, retain shared destination | `DisposeAsync_SettlesBlockedDeliveryAndLeavesSharedWriterOpen` |
| V30: publication cancellation, retained snapshot/watch, retry and cleanup | `CanceledPublication_RetainsRegistrationForRetryOrDisposal` |
| V30: terminal channel signals after delivery | `ResponseThenChannelClosure_ObservesTerminalSignalWithoutAnotherRequest` — closure and failure in both modes; two actual-Go closure traces |
| Independent actual Go handler comparison | `SourceTraces_MatchDeclaredCallbacksWatchesAndSends` — sixteen declared traces, including nine ordered ADS traces |

The shared JSON declares expected events independently. Explicit request-callback gates fill queues before the loop resumes; comparisons do not assume a winner between simultaneously ready Go events. The Go oracle enables the real `WithOrderedADS` option. Existing Go module pins and checksums retrieve the source without introducing another checkout or a submodule. The original two ADS test functions also pass unchanged in the pinned source; those reference-only checks are distinct from C# parity evidence.

## Verification and boundaries

Local macOS arm64 verification uses .NET SDK 10.0.400, native xUnit/MTP and Go 1.26.4. Complete `repository verify` passes: 198 solution cases, zero failed/skipped, zero build warnings/errors with Roslynator; all five package/symbol pairs; fresh Protocol (12), Validation (18) and Core (143) test consumers; executable Core/ASP.NET package consumers. Sixteen stream traces match the actual Go handler, alongside the existing 33 PGV, 19 snapshot, 22 cache and 14 subscription comparisons. Exact-head remote CI and independent review results belong to the implementing PR. Tests remain in the existing solution and package-consumer inventory, with no dependency or schema changes. No coverage percentage or static source/test-pairing claim is made.

T11 retains ASP.NET discovery adapters and actual HTTP/2 calls through this engine. T12/T22 retain further transport and released-Envoy acceptance. The prior gRPC/Kestrel performance penalty remains accepted; these core tests establish behavior and ownership, rather than new performance measurements. Delta, heartbeats, full optional-validator conformance and NuGet publication remain separate work.
