# T14 Delta cache and response foundation

This increment implements T14.1: SnapshotCache Delta selection/publication, raw and pass-through response envelopes, status and asynchronous watch ownership. It builds on T13 subscription state. Delta stream processing, callbacks, first-request per-type dispatch and gRPC adapters remain the next T14 increment; issue #20 stays open. The existing SotW preview's recorded scope does not change.

## Contract and implementation

Both producer methods now take caller-owned `ChannelWriter<TResponse>` destinations and return a cancellation registration. This follows the approved SotW pattern: producer writes establish delivery order, publication waits for buffer capacity outside cache synchronization, and releasing a registration must not complete a shared destination. Implement `IAsyncDisposable` when delivery owns pending work and settle it before subscription mutation.

Custom `IConfigWatcher` implementations must migrate the Delta method to:

```csharp
IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request,
    ISubscription subscription, ChannelWriter<IDeltaResponse> responses);
```

Ordinary two-argument calls remain available through `ConfigWatcherExtensions` in `Envoy.ControlPlane.Cache` and return `IWatch<IDeltaResponse>` for await-one consumption. Primary production delivery uses channels, not task-continuation ordering. Internal `ResponseWatchRegistration<TResponse>` and `TaskWatch<TResponse>` reuse the existing SotW ownership mechanics. The structural extraction passed the unchanged 152-test Core suite before feature changes.

`CreateDeltaWatch` records the node, separate Delta request time/count and constructs version maps only when a Delta watch needs them. Selection compares known per-resource versions, emits changed resources and removals, and supplies the complete next returned-resource map without mutating subscription state. The future server advances that state after sending. Initial versions are the caller-created subscription's responsibility, not read again from every request.

An existing snapshot can force an empty wildcard response when the request nonce is empty. Delayed publication never forces an unchanged empty response, even for that initial request. Changing only a system/type version does not emit unchanged payloads. ADS mode orders known resource families during publication and does not add the SotW exact-name readiness gate to Delta.

`RawDeltaResponse` lazily creates payloads and per-resource hashes and caches materialization/results or failures. It retains the system version, request and creation context. `PassthroughDeltaResponse` retains a supplied envelope and complete next-version metadata without interpreting payloads. Caller-supplied messages/envelopes/metadata remain stable for their retention lifetime. No semantic validator, deep-copy guarantee, expiry or ACK-committed ledger is introduced.

## Pinned source limits

The behavior pin is `67c5345367582741334e34ffc66a561b0ce32946`. At that revision, SnapshotCache's `createDeltaResponse` selects wildcard and exact names but **does not select prefixes**. Prefix state is still recorded; a prefix-only known resource can be reported removed. `RawDeltaResponse.GetDeltaDiscoveryResponse` **omits TTL**, even when the cached entry retains one. The comparison preserves these outcomes instead of silently claiming broader support. A pass-through envelope can retain its caller-provided fields.

These are limits of this selected implementation, not statements that the xDS protocol lacks prefix or TTL concepts. Full TTL/heartbeat work and any deliberate source-behavior change need their own evidence. The source is linked in [test attribution](../../tests/resources/UPSTREAM-TESTS.md).

## Requirement coverage

| Requirement | Evidence |
|---|---|
| Changed/unchanged/removals/system version | `DeltaCacheTests.UnchangedVersionsRemainPending_ChangedPayloadAndRemovalRespondWithoutTypeVersionChange` and the fourteen source cases exercise initial-version suppression, changed payload with identical system version, version-only changes, missing and unwatched resources. |
| Initial versus delayed empty wildcard | `EmptyWildcardResponse_IsOnlyForcedDuringCreationWithAnEmptyNonce` and `DelayedEmptyWildcardPublication_DoesNotForceAnEmptyReply`, plus actual Go comparisons. |
| Wildcard/name and prefix source behavior | `WildcardExplicitNameRemoval_ResendsAnUnchangedResource` and `PrefixOnlySelection_PreservesThePinnedSnapshotCacheLimitation`. |
| Lazy raw data and pass-through metadata | `InitialResponse_ConstructsHashesLazilyAndPreservesTheEnvelope`, `CustomSnapshot_ResponseMaterializationIsLazyAndCachesItsFailure`, `RawDeltaEnvelope_OmitsTtlAtThePinnedBaseline` and `PassthroughEnvelope_PreservesCallerMetadataAndPayloadWithoutInterpretation`. |
| Status and ownership | `DeltaStatus_SharesNodeIdentityButTracksItsOwnClockAndCount`, existing custom-watcher context/exception cases, and the pending-watch replacement of the previous unavailable-capability test. |
| Backpressure/cancellation/retry | `BlockedPublicationCancellation_RetainsAnEligibleWatchAndAllowsRetry` proves installed snapshot accessibility, retained eligibility after publication cancellation, successful retry and a still caller-owned writer. Existing SotW lifecycle tests remain unchanged. |

`SourceCacheCases_MatchDeclaredOutcomesAndExportActualObservations` checks fourteen independent declared outcomes and exports actual frames/status/lazy-map observations. `GoOracle/delta_cache.go` invokes the actual pinned SnapshotCache, subscription and raw-response APIs. It compares bytes, hashes, system versions, removals, complete returned metadata, counts and lazy maps without reproducing selection logic. The payloads are bounded, map-free RouteConfiguration fixtures; literal hash agreement in those cases is not a universal Go/C# serialization guarantee.

`repository verify` runs this comparison after the existing module-pin check and retains actual Go inputs/results under `obj/upstream/results/delta-cache/`. The complete Core suite runs against fresh NuGet packages; custom producer fixtures use the new channel contract without weakening their request/context/metadata assertions. No source/tool/binding fingerprints or package dependencies change.

Observed locally on macOS arm64: Core **165 passed**, full solution **287 passed**, zero failed/skipped and zero compiler/Roslynator warnings/errors. All fourteen cache cases matched actual Go alongside twelve Delta subscription traces and the existing corpora/stale-ACK rejection. Five package/symbol pairs, exact notices, fresh Protocol (14), Validation (18), Core (165), Hosting (31) test consumers and both executable graph consumers passed. Pack retains three intentional non-packable-project notices. Independent review and implementing-PR CI are separate gates. This increment establishes cache/response proof, not Delta network or released-Envoy acceptance.
