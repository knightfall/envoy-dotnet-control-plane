# T21 — root differential verification

Completes issue #27 / R22 for the required V05–V27 scenario families. All modes call actual Go code at behavior commit 67c5345367582741334e34ffc66a561b0ce32946; schema source remains 82a4424f760b6ba3ffc9f2911389d061656f35c8. Module checks reject a changed root/Envoy/rate-limit revision or replacement. Native fixtures assert independent expectations before exporting observed results; native-only traces never substitute for Go execution.

## Execution and retained evidence

After binding generation, run the project CLI's repository verify command. It executes native tests, every Go mode, semantic and negative comparisons, packing and fresh consumers. The artifact obj/upstream/results/root-differential/comparison.json records pins, every positive case ID/report path, negative mutation/diagnostic, the V05–V27 map and dispositions. Existing Linux/macOS/Windows CI uploads these results.

There are 16 source modes / 260 cases plus 74 constant symbols and API version. Counts describe the inventory, not a coverage threshold.

| Mode | Cases | Principal observations |
|---|---:|---|
| Snapshot/resources | 19 | Names/payloads/versions/TTL, partial graphs, consistency and lazy maps |
| SnapshotCache | 23 | Immediate/deferred/cancel/clear/readiness/retained responses and status |
| SotW subscriptions | 14 | Legacy/exact/wildcard/prefix/deactivation and returned metadata |
| Preview helpers | 8 | HCM Any extraction, references and example graph |
| Delta subscriptions | 12 | Initial versions, subscribe/unsubscribe, wildcard/prefix/exact changes |
| Delta cache | 14 | Initial/unchanged/change/removal/lazy maps and source TTL/prefix limits |
| LinearCache | 31 | Versions, update/bulk/delete/replace/index and no-op rules |
| MuxCache | 12 | Classifiers, child selection/cancellation, missing keys and Fetch errors |
| TTL/heartbeats | 13 | Wrappers/payload omission, actual source timer scan and watch counts |
| Cache Fetch | 14 | Literal/all/duplicate/missing names, opaque versions/errors and TTL |
| Shared Fetch engine | 9 | Hook/producer/materialization/error order, mutation and caller correlation |
| HTTP gateway | 30 | All eight routes, cleaned paths, JSON/status/Any/Struct cases |
| Focused ADS client | 12 | Initial/resources/ACK/NACK/error/reinitialization state |
| Conversions | 17 | Actual message/Struct semantics and complete helper symbols |
| Delta streams | 10 | Node/type/callback/drain/nonce/subscription/error/cleanup sequences |
| SotW streams | 22 | Callback/nonce/ACK/NACK/ordered-unordered ADS/error/cleanup |

## Comparison and retained negative checks

The internal comparer was renamed to DiscoveryTraceComparer after a separate build and unchanged 59-test tooling run. Both protocols use one stream-wide bidirectional nonce map. Delta request nonce is field 3; SotW request nonce is field 4. Assigned response nonces introduce mappings; subsequent sends/requests retain their associations. Callback/stale literals reserve their identity. Node, type, caller versions, initial/returned metadata, NACK/subscription fields, event order, errors and disposal counts stay exact. Events are never sorted. Delta's source driver now emits actual traces for CLI comparison rather than assuming identical counter text across runtimes.

SotW verification rejects a stale request associated with the current response. Delta verification rejects reused response nonces, including across types. DeltaTraceComparerTests accepts valid two-way renaming and rejects wrong wire/request associations, cross-type reuse, NACK/subscription/returned changes, callback order/literals and cleanup changes. Existing SotwTraceComparerTests remain. The focused pair passes 27 cases.

The maintained Go negative mode reuses existing source runners and mutates recorded outcomes only. Every check requires its exact comparison diagnostic: compile errors, missing files, parse prerequisites and timeouts cannot count as rejection. Fifteen checks cover snapshot consistency, cache lifecycle, both subscription wildcards, HCM extraction, Delta watch count, Linear version, Mux classifier count, TTL payload, Fetch version/hook order, HTTP status, client ACK version, converted Struct and conventional label. Mutated reports and diagnostics are retained. No runtime cache/client/parser/route algorithm is copied.

## Requirement evidence and dispositions

The [preview mapping](T21-PREVIEW-DIFFERENTIAL.md) retains the exact V05–V15 test/fixture table. This extends it through V27; all IDs are machine-readable in the root artifact. Native-only runtime adaptations are explicit.

| Requirement | Concrete source/native evidence |
|---|---|
| V05 | Stable replacement tests and retained-response-after-replacement re-observe actual retained responses in both drivers. Caller mutation remains unsupported. |
| V06 | Snapshot/lazy-map/serialization tests, Delta payload-change-same-system-version and system-version-only, Linear identical/no-op/prefix cases; native unknown-wire fields and runtime hash invariants. No universal cross-runtime hash claim. |
| V07 | Nineteen snapshot graphs include missing/extra EDS/RDS and partial/empty cases; Publication_DoesNotValidateOrCheckConsistency retains explicit checking. |
| V08 | Missing/publication/immediate/pending/cancel/clear cache traces; native gated registration/publication and custom producer ownership cases. |
| V09 | ADS incomplete/untracked/wildcard cache cases, ordered stream variants and ReadinessAndOrdering_AreIndependentOptions. |
| V10 | CDS/LDS full-state/deletion versus route/new-only/prefix omission, Linear selection and custom stream types. |
| V11 | All 14 transitions, including legacy-empty-stops-after-explicit, overlap/prefix/deactivation and native unsubscribe/re-subscribe. |
| V12 | Node/implied types, stale/current nonce, callback-wire-nonce and NACK-preserved versions, including ordered variants. |
| V13 | Ordered/unordered custom ADS types, buffered producer order, replacement/draining and native late-unsubscribed filtering in both modes. |
| V14 | Source cache/Delta counts across immediate/deferred/cancel/no-registration/clear; native hasher/TimeProvider retains identity/times. Absolute wall time is not equated. |
| V15 | SotW/Delta rejection/read/channel/callback order plus nine Fetch engine traces. Native primary-fault preservation, cleanup/reentry/close-once and ILogger adaptation remain. |
| V16 | Twelve Delta transitions include initial versions, subscribe/unsubscribe overlap, wildcard removal, prefix returned state and reconnect per-type state. |
| V17 | Initial-empty-wildcard, unchanged/opaque versions, removals, system-version-only and payload changes; native lazy/pass-through cases. |
| V18 | First-versions-per-type-and-stale-nack, queued response order, callback mutation/receive errors; shared nonce comparison without an ACK ledger. |
| V19 | Thirty-one Linear cases cover initial/update/delete/bulk/replace/no-op/prefix/index/version/null boundaries; native wrong-type/custom/lazy/error/ownership proof. |
| V20 | Twelve Mux cases cover classifiers, children/modes, missing keys, cancellation and custom/Linear Fetch; native original request/context/task/registration/error identity. |
| V21 | Thirteen normal/zero/absent TTL and heartbeat cases; Delta ttl-omitted remains source-matched; native caller pass-through TTL proof. |
| V22 | Actual source heartbeating uses sentinel/scan synchronization; native fake-time, interval validation, stop/disposal and pending-write cases. |
| V23 | Fourteen cache Fetch and nine engine traces cover nil envelope/missing/skip/unsupported/custom, hooks, materialization, mutation and original caller correlation. Source hook panic is normalized only by the test adapter. |
| V24 | Thirty gateway cases execute all eight Go routes, type override, cleaning, body failures and 200/304/400/404/500; native opt-in/method/auth/application-route cases. SDS fixture imports its generated descriptor explicitly. |
| V25 | Gateway/conversion cases compare proto names, enum/numeric/presence/null/Struct/TTL/Any/parser errors; native HCM/router and missing registration tests. |
| V26 | Twelve actual client traces compare accepted/received versions/nonces and error/reinit state; native queue/disposal/Kestrel channel ownership and Unavailable proof. |
| V27 | Full constants/API version, HCM/references, default/presence/uint64/Struct/Any/errors; native CLR descriptor failures and actual example graph round trips. |

No unresolved material protocol divergence was observed in this required matrix. Explicit dispositions cover stable caller ownership; asynchronous queue/publication cancellation and TimeProvider; .NET exception cleanup versus Go panic; idempotent Linear cancellation diagnostics; clock/hash representation; explicit CLR/Any registration and return-new conversion; replaced-call disposal; ILogger; and source-matched prefix/ADS/TTL limits. T05–T20 task documents record these idiomatic .NET choices. TimeSpan precision, PeriodicTimer interval constraints and stock parser/error limits remain documented runtime/API boundaries. Source/spec quirks stay source-matched and do not become stronger features.

## Verification and separate gates

Focused checks passed: unchanged 59-test tooling baseline after rename, 27 comparer tests, eight Fetch engine tests, 29 gateway tests, 30 Go gateway and nine Go Fetch engine cases, and all 15 retained negative comparisons. Full local macOS arm64 repository verify passed 467 tests, 0 failed/skipped (Core 262, hosting 104, Protocol 14, optional validation 18, tooling 69), compiler/Roslynator 0 warnings/errors, all positive/negative comparisons, five package/symbol pairs, fresh consumers (14/18/262/104 tests) and both executable package graphs. The three existing non-packable-project notices remain. SDK 10.0.400 / net10.0 / Go 1.26.4. Exact-head GitHub CI/Codex PR review remain pending merge gates. Pins, generated fingerprints and package dependencies are unchanged.

This closes the required root differential gate only. T22's full concurrency/repeated transport proof, T23's released-Envoy Delta/TTL/reconnect matrix and T26's complete validator corpus remain required separately. Performance stays manual; no packages are published.
