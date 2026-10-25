# T09 — SotW stream engine and callbacks

Implements [issue #10](https://github.com/knightfall/envoy-dotnet-control-plane/issues/10) against Go behavior commit `67c5345367582741334e34ffc66a561b0ce32946`. The [completed investigation](T09-SOTW-STREAM-INVESTIGATION.md) records the source reading and accepted gRPC/Kestrel decision. The measured transport performance penalty is accepted for now; performance tuning is not a T09 prerequisite.

## Runtime behavior

`SotwServer.StreamAsync` accepts one asynchronous request input, an awaited send delegate, an implied type URL and `ResponseContext`. An empty type URL selects ordinary unordered ADS. The caller owns the cache. Each call owns one private session and its request enumerator, linked cancellation and per-type watches.

The loop holds at most one next receive while processing, and awaits every callback/send directly. Per-type records retain subscription state and the current watch nonce. Replacing a watch cancels it before changing the subscription and starts the replacement with an empty nonce. A consumed completion is removed from selection so an immediate or filtered result cannot be selected twice. Simultaneously ready events have no promised cross-type ordering.

Normalization carries an omitted Node, captures an explicitly supplied Node and fills only an absent per-type URL. Missing ADS URLs fail before the request callback. Callbacks can change the effective request. Stale requests remain callback-visible and can change the carried Node, but do not replace a watch. NACKs preserve request versions and do not roll back snapshots.

Response processing looks up the current record through the response's originating request, filters returned-name metadata before materialization, clones the generated envelope, assigns a stream-wide decimal nonce and updates returned metadata/current nonce before the response callback and send. Callback nonce changes affect the wire without rewriting the captured nonce. Generated cloning preserves unknown fields and shares immutable `ByteString` payloads across streams.

## Failure and ownership contract

EOF, receive-operation failure and loop-selected call/host cancellation finish normally, matching the Go reader/loop. Iterator creation and disposal remain separate input/cleanup failures. Missing ADS URLs produce `SotwProtocolException` with `InvalidArgument`; absent requests/results or canceled current watches use `Unavailable`. Malformed custom response invariants use `InvalidOperationException`. Producer, materialization, callback and writer exceptions retain their identity, including cancellation thrown by those operations.

Cleanup cancels and observes the owned receive, attempts every current watch release and iterator disposal, then invokes close once, including after rejected open. A sole failure is preserved; several failures form an `AggregateException` with the processing failure first. An attempted watch disposal is not repeated if it throws. Inputs, hooks and writers must cooperate with cancellation; close must be bounded by the application. The engine has no forced cleanup timeout or detached worker.

## Requirement coverage

| Requirement | Concrete evidence |
|---|---|
| Default API and stream identity | `DefaultServer_DeliversWithoutCallbacksAndReleasesItsWatch`; `Server_AssignsDistinctIdsAcrossStreamsAndClosesEachOnce` |
| Node/type normalization and effective callback mutation | `Requests_CarryNodesAndImplyOnlyMissingTypes`; `RequestCallback_ChangesDispatchButNodeReplacementDoesNotChangeCarriedReference`; `MissingAdsType_RejectsBeforeRequestCallbackAndStillCloses` |
| Current-watch nonce lifetime, callback-visible stale requests | `StaleRequest_IsCallbackVisibleAndCapturesNodeWithoutReplacingWatch` |
| Immediate delivery, metadata/context order, callback nonce mutation | `ImmediateResponse_UpdatesMetadataBeforeCallbackAndRetainsAssignedNonce` |
| Metadata filtering before materialization | `UnsubscribedResponse_IsConsumedWithoutMaterializingOrSending` |
| Stable shared response ownership | `SharedResponse_ClonesEnvelopeAndPreservesPayloadAndUnknownFieldsAcrossStreams` |
| NACK/no rollback and callback cache reentry | `Nack_RedeliversCurrentSnapshotWithoutRollbackAndCallbacksCanPublish` |
| Serial sends and one held receive | `BlockedWriter_HoldsOneReceiveAndSerializesOtherTypeResponse` |
| EOF/receive failure/call and host cancellation | `InputTermination_ReleasesWatchesAndClosesOnce` |
| Open/request/cache/watch/materialization/hook/send failures | `OperationFailure_PreservesOriginalAndCleansUp`; `OperationCancellation_IsPreservedEvenAfterCallCancellation` |
| Protocol/custom-response invariant failures | `MissingStreamValue_ReturnsUnavailable`; `MalformedCustomResponse_FailsWithoutSendingAndReleasesWatch` |
| Cleanup error retention and iterator boundary | `CleanupFailures_RetainPrimaryAndAttemptEveryReleaseAndClose`; `EnumeratorCreationFailure_IsNotSwallowedAsReceiveFailure` |
| Independent Go loop comparison | `SourceTraces_MatchDeclaredCallbacksWatchesAndSends` and six declared traces through actual upstream `sotw.NewServer(...).StreamHandler` |

The checked-in `sotw-stream-cases.json` declares expected observations independently. Native tests assert them and export requests/actions plus observed callback/watch/send/close events. The Go oracle runs the real pinned handler and compares those events and disposal counts. Handoffs establish order explicitly; the traces do not assume a winner for simultaneously ready events or rewrite the private Go loop. Cases cover node/type normalization, stale/reset nonces, arbitrary unordered ADS types, callback-modified wire nonces, NACK versions and receive-error closure.

`repository verify` checks trace membership, verifies root/Envoy module pins, runs all comparison corpora and packs all five libraries. It also copies the complete Core suite and fixtures into a fresh package-only consumer. New code remains in the existing solution/CI and adds no package dependency, schema or generator change.

## Observed evidence and limits

Local macOS arm64 checks use .NET SDK 10.0.400, native xUnit/MTP and Go 1.26.4. Complete `repository verify` passed: 171 solution cases, zero failed/skipped; all five package/symbol pairs; fresh Protocol (12), Validation (18) and Core (116) test consumers plus executable Core/ASP.NET package consumers; zero build warnings/errors with Roslynator. Six actual Go stream comparisons and the existing validation/snapshot/cache/subscription corpora matched. Remote CI results belong to the implementing PR. A static source/test pairing helper was unavailable because its Python parser dependency was absent; no static-pairing or coverage claim is made.

The comparison reuses upstream `PassthroughResponse`, generated messages and the real SotW handler. It adapts the upstream `mockStream` fixture with its original license header. Selected C# scenarios port the upstream response, stale-nonce, missing-type, shutdown, cancellation, send-error, callback-error and nil-response tests. [Test reuse and attribution](../../tests/resources/UPSTREAM-TESTS.md) records the source revision/hash, retained license, mapping and modifications. Remaining code bridges the two languages and records observations or exercises .NET-specific ownership/cleanup; no separate test framework or substitute protocol implementation is added.

T09 establishes core portions of V10–V12/V15/V29. T10 retains ordered ADS policy, shared response draining and cross-type scheduling. T11/T12/T22 retain actual discovery endpoints, gRPC status mapping, HTTP/2 lifecycle/backpressure and released-Envoy acceptance. The transport probe exercises generated ADS/EDS calls independently; it does not exercise this engine through a service adapter. Delta, heartbeat scheduling and package publication remain separate work.
