# T19 — focused SotW ADS client

Issue #25 / R19 / V26 adds `Envoy.ControlPlane.Clients.SotwAdsClient` to Core. It uses the existing generated ADS client and `Grpc.Core.Api` dependency already supplied by Protocol. No Grpc.Net.Client/Kestrel dependency, retry/reconnect policy, subscription engine, Delta client, resource decoder or validation provider is added to Core.

## Contract and .NET adaptation

- Constructor arguments retain the caller's stable node and fixed type. `InitializeAsync(CallInvoker, CallOptions)` opens ADS and sends the initial ACK. Headers, deadline, credentials and stream cancellation follow normal CallOptions; the wrapper never owns the invoker/channel.
- `FetchAsync` returns the actual resource list as `IReadOnlyList<Any>`, retaining the received envelope privately for ACK/NACK. Receiving does not accept it. Normal EOF throws `EndOfStreamException`; producer/transport exceptions propagate unchanged. A custom reader returning null is rejected without overwriting previous state.
- `AckAsync` advances accepted state **before** sending, including when that send fails. `NackAsync` sends the accepted version and latest received nonce, with message and default status code zero. No resource names or metadata API is invented.
- Explicit reinitialization retains received state and sends ACK, so a previously NACKed last response becomes accepted on the new stream. An open failure retains the previous call; initial-send failure retains the replacement. These are observed source behaviors, not a new recovery policy.
- A SemaphoreSlim serializes complete initialize/read/ACK/NACK operations. A small lifetime lock protects only call installation/lookup/disposal; network operations and invoker creation run outside it. Reinitialization disposes the replaced call. `Dispose` synchronously cancels/disposes only the owned call and rejects further operations; callers still await their pending Tasks. No background loop or shutdown timer is introduced.
- Cancellation while queued does not mutate acceptance or cancel a different active operation. Read cancellation follows the supplied gRPC reader and may cancel that call. The library does not reinterpret errors as a reconnect request. `IsConnectionError` recognizes EOF and RpcException Unavailable/Cancelled through ordinary inner wrapping; local OperationCanceledException from queue cancellation is not a connection-loss diagnosis.

The source serializes blocking Fetch/Ack/Nack with a mutex but replaces streams outside it and does not release the previous stream. .NET async serialization and ordinary call disposal define these ownership/concurrency boundaries without copying that race/leak. Mutable node/messages/resources remain caller-owned/stable by convention. Only resource lists are public, matching the source helper's focused surface.

## Requirement-to-test mapping

| V26 behavior | Exact native test methods |
|---|---|
| Initialization, generated method/options, fixed node/type, initial fields and no implicit reads | `SotwAdsClientTests.Initialize_UsesGeneratedAdsMethodAndCallerOptions`; `BeforeInitialization_RequiresAnExplicitConnection` (Fetch/ACK/NACK) |
| Receive/ACK/NACK, repeated rejection, empty resource lists and original payloads | `ReceiveAckAndNack_TrackAcceptedVersionAndLatestNonce`; `SourceCases_MatchDeclaredOutcomesAndExportActualObservations` (12 independently specified traces) |
| Failed send advancement; receive error/null state retention | `SendFailure_StillAdvancesAcceptanceBeforeTheNextNack`; `ReceiveFailureAndNullResponse_KeepThePreviousReceivedState` |
| Reinitialization and open/initial-send failure boundaries | `Reinitialization_RetainsSourceStateAndDisposesOnlyTheOldCall`; `FailedOpen_KeepsTheOldCall_AndInitialSendFailureKeepsTheNewCall` |
| EOF, wrapped errors, selected gRPC codes and no automatic reconnect | `Eof_IsClassifiedWithoutReconnecting`; `ConnectionErrorClassification_UsesTheSourceGrpcCodes` |
| Async serialization, local cancellation, pending read disposal and use-after-dispose | `SotwAdsClientLifecycleTests.PendingRead_SerializesAckUntilTheResponseArrives`; `CancelingQueuedAck_DoesNotAdvanceAcceptanceOrCancelTheActiveRead`; `Dispose_CancelsTheOwnedReadAndRejectsQueuedAndFutureOperations` |
| Real HTTP/2 updates/ACK/NACK/channel ownership and errors | `SotwAdsClientIntegrationTests.SnapshotUpdates_AckAndNackRoundTrip_AndDisposalLeavesTheExternalChannelUsable`; `ServerUnavailable_IsExposedWithoutAutomaticReconnection` |

The scripted CallInvoker records the generated method/options and outgoing request attempts, supplying response/error/EOF steps. It supplies transport behavior, not the client's acceptance state. Positive channel handshakes make the three lifecycle cases deterministic; timeouts bound failure, no sleeps/stress loop are used. Loopback reuses the existing Kestrel host, generated contracts, callbacks and snapshots. No hard-coded upstream test port or new test server framework is copied. Static source-pairing is only a heuristic, not coverage evidence.

## Pinned source and comparisons

Source behavior remains `67c5345367582741334e34ffc66a561b0ce32946`. Copyright 2022 Envoyproxy Authors / Apache-2.0 attribution is retained, with the original license already packaged. Actual files inspected:

| Source file | SHA-256 |
|---|---|
| `pkg/client/sotw/v3/client.go` | `5bd59939f18e32250c8d3daf5e9c3bd479b5a21df21d2dd6bf7126afa9e2cfcc` |
| `pkg/client/sotw/v3/client_test.go` | `21f82f9bc706d757f148ca6cb38641ac9cd53d2737228df794c783f7352ea4d5` |

`GoOracle/ads_client.go` calls the actual pinned `NewADSClient` through grpc.ClientConnInterface/ClientStream. Its fixture records sends and supplies wire messages/errors, without implementing ACK/NACK/reinit/selection logic. Twelve native traces declare expected request fields/errors independently and export actual observations. Go compares every request's call index, accepted version, nonce, error presence/message/code, resource Any bytes and normalized error/connection classifications. Node, fixed type and empty resource names are checked directly on both sides. No order/nonce normalization is used. Source stream-leak/disposal behavior is outside that protocol comparison and has explicit native ownership proof.

`repository verify` requires the complete unique trace inventory and retains Go observations under `obj/upstream/results/ads-client/go-results.json`. Existing source/schema/generator pins, runtime package graph and optional-validation separation are unchanged. Focused local checks passed 19 native client/lifetime cases, 12 actual Go traces and 2 Kestrel integration cases. Full local macOS arm64 `repository verify` passed **449 tests, 0 failed/skipped** (Core 254, hosting 104, Protocol 14, optional validation 18, tooling 59), compiler/Roslynator **0 warnings/errors**, all existing Go corpora plus the twelve client traces, five package/symbol pairs, fresh consumers (14/18/254/104 tests) and both executable package graphs. The three existing non-packable-project notices remain. An injected ACK version was specifically rejected with `ack-flow: ADS client differs`. Toolchain: SDK 10.0.400 / net10.0 / Go 1.26.4. GitHub CI and native Codex PR review remain separate pending merge gates. Full T21/T22/T23/T26 acceptance remains separate. The gRPC performance baseline stays manual and no NuGet packages are published.
