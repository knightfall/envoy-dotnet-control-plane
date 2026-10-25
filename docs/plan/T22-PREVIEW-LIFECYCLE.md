# T22 — Preview concurrency and lifecycle evidence

Implements the preview subset of [issue #15](https://github.com/knightfall/envoy-dotnet-control-plane/issues/15). Existing deterministic cache/stream tests and the pinned [T21 Go comparisons](T21-PREVIEW-DIFFERENTIAL.md) remain the foundation. The additions exercise missing cancellation boundaries and repeated real HTTP/2 calls. No runtime API, coordination policy, dependency, schema, generated fingerprint or package lock changes.

## Deterministic boundaries

`SotwBlockedWriterCancellationTests.CallCancellation_SettlesBlockedWriterAndBothTypeWatches` holds the first writer inside an awaited operation, queues a response for another type, then cancels the call. Both modes finish the Core task, dispose both registrations and the input exactly once, close once and send no second response. The existing fault policy is preserved: ordinary mode retains the cancellation exception; ordered main-queue delivery reports `Unavailable` with that exception as its cause. The test does not redesign transport cancellation status.

`ActiveCallbackShutdownTests.ApplicationStopping_CancelsActiveCallbackBeforeStopDeadline` covers request and response callbacks in both modes. Each scenario first registers two types, reenters publication from the callback, then blocks cooperatively. The test signals `ApplicationStopping` directly and observes callback cancellation, close-once and zero watches while `ApplicationStopped` is still unset, before calling any host stop operation. The callback's release gate remains unset throughout. Neither client cancellation nor the fixture's two-second shutdown bound can produce those observations. This supplements T11's eventual bounded-stop test with a direct host-lifetime cancellation proof.

## Repeated transport run

`SotwStressTests.RepeatedAdsRounds_ReleaseEveryWatchAndRecordMemory` uses seed **43622**, sixteen rounds, four reused nodes and two resource types per node. Each mode runs **64 streams** and checks **256 responses** over actual loopback HTTP/2. Initial publication mixes immediate and deferred responses; each stream ACKs both types, receives a replacement and either closes normally or cancels. The fixed seed yields 34 normal closes and 30 cancellations per mode. Node plans are chosen before concurrent workers start, so the random generator is never shared between workers.

Registration gates expose exact pending-watch states before publication and after ACKs. Every response is checked for type, version, payload and a unique nonempty stream nonce; each ACK uses the nonce actually received for that type. All worker tasks complete within bounded failure waits. Every round observes one close per node, exact callback/request counts, unique stream IDs and zero pending watches. Observer queues are drained. The test explicitly clears its own snapshots/status between rounds; this is test ownership cleanup, not a claim that normal stream closure removes cache status.

Each round records estimated managed bytes, process working set and watch count. Reports also include seed, mode, platform/runtime/architecture, stream/response counts, close/cancel counts and elapsed duration. These process-wide samples may include other test activity and normal runtime allocation/GC effects. They are diagnostics, not a retained-memory leak proof, throughput result, new memory budget or fairness SLA. The measured finite run supplements the deterministic boundary tests.

The repository verifier copies both JSON reports from the hosting output to Protocol's `obj/upstream/results/lifecycle/`, already included in CI artifacts. The same tests also run against freshly packed packages. No timing-based success rule or swallowed timeout is used; an expired failure wait fails the test.

## Requirement coverage

| Requirement | Exact evidence |
|---|---|
| V14/V30: register/publish interleaving | Existing `SnapshotCacheConcurrencyTests.RegistrationAndPublication_RetainReplacementWhenLookupIsInProgress` |
| V30: bounded destination, publication order, cancellation, multiple nodes | Existing `SnapshotCacheChannelTests.FullDestination_AwaitsSpaceOutsideCacheLockAndSerializesPublications`, `QueuedPublication_CancelsBeforeInstallationWithoutWaitingForAnotherConsumer`, `DisposeAsync_SettlesBlockedDeliveryAndLeavesSharedWriterOpen`, `CanceledPublication_RetainsRegistrationForRetryOrDisposal` |
| V12/V13/V30: send/unsubscribe and stale response | Existing `SotwOrderedAdsTests.LateUnsubscribedResponse_IsFilteredAgainstReplacementSubscription`, `Replacement_DrainsOtherTypesAndDropsSupersededCurrentType`, `StaleRequest_DoesNotDrainOrReplaceItsWatch`; actual pinned Go queue/drain traces remain in T21 |
| V15/V29: custom producer and callback failures | Existing `SotwLifecycleTests.OperationFailure_PreservesOriginalAndCleansUp`, `MissingStreamValue_ReturnsUnavailable`, `MalformedCustomResponse_FailsWithoutSendingAndReleasesWatch`, `CleanupFailures_RetainPrimaryAndAttemptEveryReleaseAndClose`; T21 compares six rejection scripts with actual Go |
| V29/V30: cancel while writer is blocked | New `CallCancellation_SettlesBlockedWriterAndBothTypeWatches` in both modes |
| V15/V28/V29: active callback, reentry and host lifetime cancellation | New `ApplicationStopping_CancelsActiveCallbackBeforeStopDeadline`, four request/response and ordering combinations; existing `SotwResponseTests.Nack_RedeliversCurrentSnapshotWithoutRollbackAndCallbacksCanPublish` retains same-node reentry proof |
| V28: actual methods and status | Existing `DiscoveryEndpointTests.PreviewService_RoundTripsSnapshotOverHttp2AndClosesItsWatch` for all nine methods, `HostingLifecycleTests.CoreRejection_MapsStatusAndClosesTheCall`, `ClientCancellation_ReleasesWatchAndLeavesCallerChannelUsable`, `DeferredMethods_RetainGeneratedUnimplementedStatus` |
| V14/V28–V30: repeated progress, exact close/watch counts and diagnostic memory trends | New `RepeatedAdsRounds_ReleaseEveryWatchAndRecordMemory`, both modes; deterministic assertions accompany the JSON reports |

## Verification and boundaries

Complete local macOS arm64 `repository verify` passes **252** solution cases, zero failed/skipped, zero compiler/Roslynator warnings/errors, all five package/symbol pairs, fresh Protocol (12), Validation (18), Core (149), Hosting (31) test consumers and executable Core/ASP.NET consumers. All existing Go comparisons and the injected-mismatch rejection pass. Both retained lifecycle reports contain sixteen zero-watch round samples, 64 streams and 256 responses per mode. Pack emits the existing two notices for non-packable example/probe projects. Exact-head CI/Astra review results belong to the implementing PR. SDK 10.0.400, Go 1.26.4 and existing immutable source/tool pins are unchanged.

The preview has no heartbeat timers. Full Delta/Linear/Mux/timer concurrency remains the separate 1.0 T22 task. Released-Envoy traffic/reconnect proof remains T23/T12. Existing gRPC/Kestrel performance acceptance is unchanged. No static pairing or coverage percentage is claimed because the optional pairing parser prerequisite remains unavailable.
