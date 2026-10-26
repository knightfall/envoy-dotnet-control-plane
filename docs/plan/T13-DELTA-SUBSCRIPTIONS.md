# T13 Delta subscription state

`DeltaSubscription` implements the existing `ISubscription` view with stream-owned ordinal sets/dictionaries. It matches the pinned Go `pkg/server/stream/v3/subscription.go` transitions, using ordinary .NET collections and read-only input contracts. It adds no locks, ACK ledger, state-machine framework or response-selection policy. Delta cache/watch/server delivery remains T14.

## State and initialization

Construct a state once for the first request of each type, passing that request's initial resource versions and legacy-wildcard setting. Later requests call `UpdateResourceSubscriptions`; that method accepts additions/removals and cannot re-seed initial versions. `SetReturnedResources` replaces the current metadata after delivery. A new stream/type gets a new instance for reconnect initialization. The future T14 server must own that per-type dispatch; T13 does not claim a running Delta server.

`IReadOnlyCollection<string>` fits protobuf `RepeatedField<string>` and exposes count without allocating or enumerating a request twice. Initial and returned dictionaries are copied, consistent with the SotW API: a caller can provide read-only dictionaries without handing mutable bookkeeping to the stream. Returned views are retained for a watch lifetime; cancel/settle the current watch before mutation. This is ordinary C# ownership rather than copying Go map aliasing.

Preserved transitions:

- Initial empty additions enable legacy wildcard only when allowed. An unsubscribe-only legacy request is ignored, including wildcard removal, until the first nonempty addition.
- The first nonempty addition permanently disables legacy fallback. An already active wildcard remains until explicitly removed; a nonempty initial subscription starts with wildcard off unless `*` is present.
- Additions precede removals, so a name/prefix/wildcard in both lists ends removed.
- Exact names and `collection/*` prefixes remain separate. Removing a prefix preserves returned metadata, matching the source.
- Removing an explicitly subscribed exact name while wildcard remains active writes an empty returned version. The later cache can send the resource again or report its removal. Other exact removals delete returned metadata; re-subscribing can therefore return an unchanged resource.
- Returned metadata replacement does not alter subscriptions. No SotW response-matching or stale-nonce policy is added to Delta.

## Proof and provenance

The twelve independently expected traces in `delta-subscription-cases.json` cover legacy on/off and deactivation, unsubscribe-only behavior, exact/prefix/wildcard changes, overlapping lists, returned-state retention/replacement and fresh reconnect seeding. `DeltaSubscriptionTests.SourceDerivedTransitions_MatchEveryDeclaredState` checks every state and exports actual C# observations. Two focused native cases verify read-only input ownership and separate type/reconnect initialization.

`GoOracle/delta_subscription.go` calls the actual pinned `NewDeltaSubscription`, `UpdateResourceSubscriptions` and `SetReturnedResources` APIs. It shares the existing observation helper and compares every state without rewriting the subscription algorithm. `repository verify` checks declared trace membership, verifies the existing immutable Go module pins and runs the comparison. Results are retained under `obj/upstream/results/delta-subscriptions/` and CI's existing artifacts. The full Core suite also runs against freshly packed NuGet packages.

The behavior source is commit `67c5345367582741334e34ffc66a561b0ce32946`. Source SHA-256: `7d5cf20175a7fbb6b9132f42a058843e2727b6709f828a6fbf9cd31ebeaa7fd4`; subscription-test SHA-256: `bd40de6ab8f12feba10254167a06e4b20b91f77fcac4e1c0c7c63e167be96ca0`. The C# adaptation retains attribution, and the upstream Apache-2.0 license remains packaged with Core. [Test reuse](../../tests/resources/UPSTREAM-TESTS.md) records the selected upstream cases and pinned module arrangement.

The added three native cases bring Core to **152** and solution discovery to **274**; tooling remains 59. Implementing-PR evidence records observed local/CI results and independent Sol xhigh review. This is V16 subscription-state evidence, separate from T14 Delta runtime/HTTP2 and released-proxy acceptance. The T12 SotW preview manifest retains its original evidence commits and scope.

Observed locally on macOS arm64: Core **152 passed**, full repository **274 passed**, zero failed/skipped and zero compiler/Roslynator warnings/errors. All twelve Delta state traces matched actual Go results, alongside the existing corpora and stale-ACK mismatch rejection. Five package/symbol pairs and fresh Protocol (14), Validation (18), Core (152), Hosting (31) test consumers plus executable consumers passed. Generation/source/binding pins and fingerprints remain unchanged. Solution pack retains three intentional non-packable-project notices; remote implementing-PR CI is separate evidence.
