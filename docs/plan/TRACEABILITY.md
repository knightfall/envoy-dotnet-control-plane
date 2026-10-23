# Requirements, verification cases, and release gates

Reviewed **24 October 2026**. All verification below is **planned**, not executed. The [implementation plan](IMPLEMENTATION-PLAN.md) owns task definitions; the [architecture](../architecture/ARCHITECTURE.md) owns proposed behavior. The [parity matrix](PARITY-MATRIX.md) bounds the compatibility claim.

## Requirements

Preview includes the eight standard SotW resource families and ADS; ratelimit configuration SotW/unary joins the complete 1.0 surface. Rows marked both require preview coverage only for the preview subset. Optional rows cannot silently become root-release blockers or root-release claims.

| ID | Requirement / component | Scope | Tasks | Verification | Gate |
|---|---|---|---|---|---|
| R01 | Complete immutable Go/schema/import/ratelimit provenance | Both | T01, T24, T25 | V01, V38 | G0, G5 |
| R02 | One reproducible, consumable binding provider with required RPC contracts | Both | T02, T04 | V02, V04, V37, V38 | G1, G5 |
| R03 | Resource identity, TTL entries, immutable snapshots, opaque versions | Both | T05 | V05, V06, V07, V21 | G2, G3 |
| R04 | Usable standard extension catalog, custom resources and descriptors | Both | T02, T05 | V03, V04, V25 | G1, G3 |
| R05 | SnapshotCache publication/get/clear, node hashing and source readiness | Both | T07 | V07, V08, V09, V10 | G2, G3 |
| R06 | Custom watcher/snapshot/fetch contracts, one-shot delivery and cancellation | Both | T06, T22 | V08, V29, V30 | G2, G4 |
| R07 | SotW named/prefix/wildcard transitions and legacy option defaults | Both | T08 | V10, V11 | G2 |
| R08 | SotW state, selection, stale nonces and per-type adapters | Both | T09 | V10, V12, V28 | G2 |
| R09 | SotW ADS dispatch/readiness and optional ordered behavior | Both | T10 | V09, V13, V30 | G2 |
| R10 | In-path callbacks, error rejection, close-once and mode-specific order | Both | T09, T14, T22 | V12, V15, V18, V29 | G2, G4 |
| R11 | Safe cache status: node, counts and request times | Both | T07, T22 | V14, V30 | G2, G4 |
| R12 | Delta initial versions and exact/prefix/wildcard unsubscribe semantics | 1.0 | T13 | V16 | G3 |
| R13 | Delta resources/removals, per-type/ADS adapters, source nonce behavior | 1.0 | T14 | V17, V18, V28, V32 | G3, G4 |
| R14 | Linear updates/version prefix/watch indexes and unsupported Fetch | 1.0 | T15 | V19, V30 | G3 |
| R15 | Mux separate classifiers, delegation, missing-key errors | 1.0 | T16 | V20 | G3 |
| R16 | TTL encoding, source SotW heartbeat scheduling and disposal | 1.0 | T17 | V21, V22, V33 | G3, G4 |
| R17 | Shared fetch semantics and all supported unary methods | 1.0 | T18 | V23, V28 | G3 |
| R18 | Eight HTTP routes, protobuf JSON/Any and HTTP status behavior | 1.0 | T18 | V24, V25 | G3 |
| R19 | ADS helper initialization/receive/ACK/NACK/error/ownership | 1.0 | T19 | V26 | G3 |
| R20 | Resource/well-known/reference/Struct helpers and ILogger mapping | Both | T20, T11 | V27, V15 | G2, G3 |
| R21 | Host DI/lifetimes, HTTP/2 transports, shutdown and example | Both | T11, T22 | V28, V29, V31 | G2, G4 |
| R22 | Go differential traces and actual released Envoy compatibility | Both | T12, T21, T22, T23, T25 | V05–V33 | G2, G4 |
| R23 | Protovalidate candidate/annotation/ownership decision | Design proof | T03 | V34, V35 | G1 |
| R24 | Optional validated publication, conformance and isolated evaluator | Optional | T26 | V34, V35, V36, V37 | GV |
| R25 | Build/package/clean consumption, CI, docs and update workflow | Both | T04, T24, T25 | V01, V37, V38 | G5 |
| R26 | Separately claimed matcher/contrib/legacy extension scope | Optional | T27, T28 | V39, V40, V37, V38 | Extension gate |

## Verification case catalog

Each ID is a scenario family with positive, negative, and boundary examples; it is not a single superficial assertion. Fixture IDs below should become machine-readable scenario IDs in T21. Evidence includes expected and observed outcomes, exact pins, and failure diagnostics. No test count target substitutes for outcome coverage.

| ID | Planned scenario and required observation |
|---|---|
| V01 | Provenance: resolve every selected source/import/tool; compare baseline to checkout and schema pin; detect intentionally changed/missing lock entries. |
| V02 | Binding proof: compile/pack/restore exact provider catalog; instantiate required messages and server/client contracts from a clean project. |
| V03 | Common Any graph: listener/HCM/router/TLS and ratelimit configuration survive semantic binary and JSON round trips with required descriptors; custom extension works. |
| V04 | Type identity: duplicate conflicting descriptor/CLR ownership is detected; one provider and one annotation definition set load in a clean consumer. |
| V05 | Ownership: mutate original and retrieved messages after publication; built-in snapshot bytes/version remain stable; custom ownership rules are explicit. |
| V06 | Versions: same/changed payload and version-prefix relationships, caller version behavior, lazy hashes and unknown binary fields; no assumed Go/C# hash equality. |
| V07 | Snapshot consistency: partial/empty graphs, missing/extra EDS/RDS references, explicit check failures; SetSnapshot does not invoke checks automatically. |
| V08 | Watch/publication: no snapshot, immediate response, same-version pending, register/update interleaving, custom responses, cancel/double-cancel, lost-wakeup detection. |
| V09 | ADS readiness: partial names, missing requested resource no-registration source edge, later publication, consistent snapshots and independent readiness/order settings. |
| V10 | SotW selection: cluster/listener full-state rules versus selected resource families, empty results, named/prefix response metadata and custom resource types. |
| V11 | SotW subscriptions: initial empty legacy wildcard, explicit names then empty, `*`, prefixes, overlapping names/prefixes, unsubscribe/re-subscribe, global/per-type deactivation. |
| V12 | SotW protocol: node omission/later replacement, implied/absent type URL, matching/stale nonce after request callback, ACK/NACK and version relationships; no automatic rollback. |
| V13 | SotW ADS: default unordered versus ordered drains/priority, independent type progress, stale unsubscribed response suppression, arbitrary type URL support. |
| V14 | Status: copied node identity, last SotW/Delta request times using controlled clock, counts for immediate/deferred/canceled/no-registration/clear paths. |
| V15 | Callbacks/logging: open/request rejection, response notification ordering, fetch hooks, close exactly once on rejected open/failure, reentry and explicit .NET fault policy. |
| V16 | Delta subscriptions: first initial versions per type, ignored later initialization, exact/prefix/wildcard changes, subscribe+unsubscribe ordering and wildcard explicit-name unsubscribe. |
| V17 | Delta cache responses: initial empty wildcard response, unchanged suppression, changed versions, removals, pass-through responses, lazy version maps and partial snapshots. |
| V18 | Delta protocol: drain before request mutation, source callback/node/type order, unique nonces, NACK/stale nonce traces without invented SotW filtering or ACK ledger. |
| V19 | LinearCache: initial/update/delete/bulk/replace/no-op/type mismatch, version prefix, indexed subscriptions in both modes, custom resources and unsupported Fetch. |
| V20 | MuxCache: separate classifiers, multiple child cache types, missing key, child failure/cancel propagation, fetch delegation and Linear limitation. |
| V21 | TTL encoding: optional durations, resource wrappers, normal payload and heartbeat omission, SotW/Delta versions and no automatic cache expiry. |
| V22 | SotW heartbeat timer: fake-clock tick, TTL-only selection including source prefix behavior, current version, unchanged returned metadata, consumed watch and next request; stop/dispose race. |
| V23 | Fetch: callback sequence, selected type/version, nil/missing snapshot/not-modified/custom fetch result, Linear unsupported and Mux errors; unary standard+ratelimit inventory. |
| V24 | HTTP: all eight cleaned paths, unknown path, missing/unreadable/invalid body, forced type URL, 200/304/400/404/500 and empty 304 body; verb/limits departures explicit. |
| V25 | Protobuf JSON: proto names, enum/numeric/presence/unknown-field behavior, Struct, registered/unregistered Any, malformed payloads and Go parser/formatter comparisons. |
| V26 | Client: initial request, receive, ACK advance, NACK prior accepted version/latest nonce, error categories, use-before-init/reinit, concurrent operations and channel ownership. |
| V27 | Helpers: name/type/reference/well-known symbol inventory, HCM Any references, message/Struct round trips, null/default/presence and custom descriptor errors. |
| V28 | Transport matrix: actual HTTP/2 gRPC streaming/unary calls for every supported method, ADS missing type error, unsupported service inventory, cancellation and failed writes. |
| V29 | Lifecycle: open rejection, request rejection, EOF/read/write/materialization/callback failure and host stop; all watches/tasks/timers end and close is called once. |
| V30 | Concurrency: deterministic race schedules, blocked writer/callback, callback publication reentry, unrelated-node progress, bounded-capacity behavior and repeated leak/stress checks. |
| V31 | Real Envoy SotW/ADS: pinned released proxy bootstrap, initial traffic, endpoint/route update, deliberate NACK, reconnect and eventual config/traffic convergence. |
| V32 | Real Envoy Delta/ADS: update and removal, reconnect with initial versions, resubscribe and expected traffic/config dump state. |
| V33 | Real Envoy TTL: demonstrate refresh prevents expiry, then expiry occurs when refresh stops, without conflating proxy TTL and cache deletion. |
| V34 | Validator candidates: pinned restore/descriptor coverage and representative cases at G1; official conformance with complete pass/fail/skip and concurrency/lifecycle evidence at GV. |
| V35 | Migration: rule inventory, pinned controlled overlay, normalized descriptor field/service invariance, binary compatibility, original Go PGV versus migrated .NET rule outcomes. |
| V36 | Validated publication: success, violation, missing/unsupported metadata, CEL fault, cancellation, diagnostic limits; all-or-nothing installation and default disabled behavior. |
| V37 | Package consumer: clean restore/build/use for advertised targets; no duplicate definitions, correct symbols/notices, application channel ownership, no CEL in Core-only graph. |
| V38 | Reproducibility/update: regenerate selected catalog without unexplained changes, detect drift, report old/new behavior/schema pins and retain historical evidence. |
| V39 | Optional matcher: source-matched tree evaluation, custom registry, legacy conversion, unknown extension and failure behavior for the stated subset. |
| V40 | Optional catalog: named contrib/legacy inputs, no overlapping ownership, descriptor/binary/Any/JSON and clean-consumer proof; no inferred legacy server implementation. |

## Release gates

| Gate | Required evidence | Blocks |
|---|---|---|
| G0 — source provenance | T01/V01 complete input origins including ratelimit; recorded baseline | Binding acceptance |
| G1 — binding and metadata decision | T02 clean generate-or-restore/pack/consume; catalog/descriptor ownership; T03 bounded validation assessment with explicit metadata decision | Public binding/Core API freeze |
| G2 — preview | T04–T12 completed for preview scope; preview helper subset, differential cases, lifecycle/transport, V31 and clean consumption | Any preview publication |
| G3 — root feature coverage | T13–T20 complete, all mandatory matrix rows mapped to adapters/cases, T21 source comparisons with no unresolved material divergence | Root parity claim |
| G4 — interop and resilience | T21–T23 full root evidence, V28–V33, supported Envoy images, no unresolved cleanup/progress or protocol defects | Release candidate readiness |
| G5 — releasable packages | G0–G4, T24/T25, clean consumers/CI/docs/compatibility manifest and API review | 1.0 publication readiness |
| GV — optional validation | T26, V34–V37, exact provider/migration/corpus pins, semantic difference report, isolated evaluator dependency | Envoy validation compatibility claim |
| Extension gate | Selected T27/T28 cases, package identity and accurate separate scope | Matcher/catalog claim |

Gate evidence is tied to an immutable candidate commit and exact dependencies. A changed candidate reruns affected gates; earlier evidence can be reused only with a documented unchanged-input justification. Publication requires an authorized workflow independently of a readiness result.

## Compatibility report template

At release, create `docs/compatibility/<release>.md` with:

- Candidate package/source versions, Go behavior commit, Envoy schema and external import/ratelimit revisions.
- Binding provider, generator/runtime versions, catalog and descriptor manifest digests.
- Supported .NET targets/platforms and actual proxy image digests tested.
- Each requirement/matrix row's implemented/unsupported/optional status and evidence links.
- Source/spec divergences, intentional .NET API/host departures, hash/version limitations, cancellation/ownership contracts.
- Validation provider/metadata/corpus status; explicit “not included” if GV was not run/passed.
- Commands and observed outcomes, known defects, unexecuted scenarios, and package-consumption results.

## Planning-document audit

For the design deliverable, check local links, unique IDs, valid dependency references, an acyclic task graph, requirement-to-task/case/gate coverage, and provenance against the source checkout. This audit establishes documentation consistency only. It does not satisfy the implementation gates above.
