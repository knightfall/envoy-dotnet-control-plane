# Architecture decision register

Reviewed 24 October 2026. **Recommended** means a design default for implementation, not an approval attributed to the user. **Proof required** means the specified experiment must close before the decision is frozen. All decisions remain revisable with recorded compatibility consequences.

The user has required feature availability to stop at parity and included optional schema validation. ADR10 records early validation feasibility and a separate package gate; ADR13 retains the ceiling for other features. Implementation choices remain recommendations within that scope.

See [architecture](ARCHITECTURE.md), [validation](VALIDATION.md), and [implementation tasks](../plan/IMPLEMENTATION-PLAN.md).

| ID | Decision | Status | Closure / implementation |
|---|---|---|---|
| ADR01 | Target maintained root-library v3 behavior, with a SotW preview | Recommended | T01, T12, T25 |
| ADR02 | .NET 10 runtime and Protocol targets; broaden only for a named consumer | Protocol candidate proved locally | T02, T04 |
| ADR03 | Protocol/Core/ASP.NET packages plus optional validation package | Recommended | T02, T03, T04, T26 |
| ADR04 | Exactly one binding and descriptor owner, compatible with validation metadata | Proof required | T02, T03; G1 |
| ADR05 | Stable published resources under the Go lifetime convention | Recommended | T05, T07 |
| ADR06 | One-shot watches, one event loop/writer per stream, no network awaits under cache locks | Recommended | T06, T09, T14, T22 |
| ADR07 | Preserve source defaults and mode-specific state transitions | Recommended | T08–T10, T13–T17, T21 |
| ADR08 | Treat versions/nonces as opaque; no universal Go/C# hash promise | Recommended | T05, T21 |
| ADR09 | Include unary Fetch and HTTP gateway in 1.0 | Recommended | T18 |
| ADR10 | Optional schema validation; feasibility before binding freeze, conformance before validation claims | User-approved scope; provider proof required | T03 at G1; T26/GV |
| ADR11 | Separate behavior/schema/released-proxy pins and evidence | Recommended | T01, T02, T23–T25 |
| ADR12 | In-memory library with caller-owned hosting policy, storage, and channel | Recommended | T07, T11, T19 |
| ADR13 | Feature availability stops at selected Go runtime and schema-validation parity | User-required scope ceiling | All active tasks; T25/T26 scope review |

## ADR01 — Measurable release scope

**Reason:** The Go root module implements more than snapshot streams: client/fetch/helper APIs are part of a credible parity claim. A small preview gives early integration feedback without treating an incomplete release as full parity.

**Alternatives:** Streaming-only as permanent scope would be simpler but would require a narrower product claim. Whole-repository parity would add matcher, catalogs, and generated APIs unrelated to root runtime behavior. Those extensions have their own tasks and gates.

**Consequence:** Every matrix row maps to tasks and verification. Unsupported upstream operations remain documented. Consumers can request matcher/contrib/older target scope before package publication; no such requirement is assumed.

## ADR02 — Runtime target

**Reason:** Recommend the supported .NET 10 runtime for a new library and use its async/runtime facilities. Older runtimes add a support matrix without a named consumer in the current requirements. Confirm policy/version details at implementation kickoff against [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

**Alternative:** Multi-target Core immediately. Revisit if an actual consumer requires it. A broader Protocol target is useful only if it compiles and restores with the chosen protobuf/gRPC toolchain.

**T02 outcome:** Protocol targets net10.0; no older-runtime consumer requirement exists. Its candidate is generated through Buf CLI with centrally managed package dependencies. See the [binding proof](../../schemas/BINDINGS.md) for clean consumption, unchanged schema identity and the three C# generator exceptions. T03 still gates provider freeze.

**Consequence:** T04 pins the SDK. The eventual compatibility report names targets; documentation cannot imply all .NET versions.

## ADR03 / ADR04 — Package boundaries and generated identity

**Reason:** Applications may use caches without a web host. Generated messages also serve code outside the control-plane runtime. A single authoritative message assembly prevents duplicate CLR types and descriptor registration conflicts.

**Alternatives:** A monolith couples all consumers to hosting/validation dependencies. Multiple overlapping generated catalogs cause ambiguous types. A third-party or Buf SDK is acceptable only after exact input/service/descriptor and clean-consumption proofs; availability alone is insufficient.

**Consequence:** Keep the three root packages and a separate optional validator. If an external provider owns the messages, Protocol becomes a dependency facade rather than regenerating overlapping classes. T02 proposes binding/annotation ownership; T03 proves validation feasibility before G1 freezes it. The T03 evaluator consumes those same message instances through IMessage and references the shared annotation owner; it owns evaluator dependencies. The annotation-only package contains no CEL/evaluator dependency. Any derived annotation overlay must preserve wire identity and have recorded provenance. An incompatible provider change after publication may require a major version.

## ADR05 — Resource lifetime safety

**Reason:** Lazy bytes/hashes require stable published protobuf messages. Follow the Go lifetime convention and document that callers publish replacement messages rather than mutating retained ones.

**Alternative:** Mandatory defensive copying on publication and retrieval would add allocations and stronger guarantees than the selected parity contract. It is not part of the release design.

**Consequence:** Verify stable-resource use and explicit replacement/version behavior. Do not promise mutation isolation or add an advanced ownership-transfer API. Copy only where a specific upstream operation or concurrent lifetime requires it.

## ADR06 — Asynchronous watches and stream ownership

**Reason:** Go already provides a small processing loop with per-type watch records. Preserve that structure using ordinary .NET asynchronous I/O. Cache result delivery must remain separate from network writes; per-stream ownership serializes nonce/subscription state and sends.

**Implementation choice:** Select a task or channel watch representation during the SotW slice. A dedicated writer service, generalized dispatcher, mandatory bounded mailbox, configurable quotas, and mandatory generation identifiers are not requirements. Add an internal identity check only where an actual queued-completion race requires it. Preserve source-equivalent draining and backpressure.

**Consequence:** Immediate completion, cancellation races, blocked writers, and callback reentry are required cases. Async response/close callback faults have an explicitly documented .NET policy because the source hooks do not return errors.

## ADR07 — Fidelity before protocol improvements

**Reason:** Current defaults are unordered SotW ADS and legacy wildcard enabled. Source Delta does not implement the SotW stale-nonce branch or an ACK-committed ledger; the cache heartbeat scheduler scans SotW watches. An apparently cleaner common implementation could change these behaviors.

**Alternative:** A generalized engine built from the specification alone cannot establish compatibility with the pinned Go baseline. Keep each mode's direct processing path.

**Consequence:** Keep source/spec divergences visible in fixtures. `Snapshot.Consistent` and validation remain explicit. The unusual ADS missing-resource no-registration case needs a recorded decision before changing it. Ordered ADS remains a SotW option, not a global transaction.

## ADR08 — Version and serialization compatibility

**Reason:** Deterministic protobuf serialization is not canonical across languages. Exact SHA256 identity cannot be inferred from semantic resource equality. [Protobuf guidance](https://protobuf.dev/programming-guides/serialization-not-canonical/)

**Alternative:** Require byte/hash identity for every resource. That would need a proven canonical serialization/versioning contract beyond this library's current scope.

**Consequence:** Preserve equality/change/stale relationships in differential comparison and the version options already supplied by Go. Do not add a public pluggable version-strategy API to handle an unproven interoperability concern. Never normalize relationships independently per message. Report literal-hash limitations in the release compatibility manifest.

## ADR09 — Fetch transport scope

**Reason:** Unary gRPC methods and eight HTTP gateway routes still exist in the reviewed source. Their shared cache/callback path is small relative to the protocol engine. The undated deprecation note does not remove their parity requirement.

**Alternative:** Defer HTTP forever; this narrows the claim and leaves existing Go consumers without equivalent behavior.

**Consequence:** Endpoint registration is opt-in. Preserve 304 and protobuf JSON behavior, LinearCache's fetch limitation, and the source RPC asymmetries for VHDS/LEDS/ratelimit configuration.

## ADR10 — Optional schema validation

**Reason:** Validation constraints form part of schema compatibility. Preserving wire fields alone does not preserve those rules. The user has included optional validation and an early feasibility check in scope. The target is the selected upstream Go PGV semantics; Protovalidate is a candidate pending proof.

**Boundary:** Original validation annotations and descriptors ship from the first binding release. T03 assesses candidates, metadata access, single-owner descriptors, and representative Go/.NET outcomes before G1 binding freeze. Full T26 conformance is gated by GV and does not block the streaming preview or root-runtime parity claim.

**T03 source decision, 25 October 2026:** The user selected the [pinned source-built ProtoValidate arrangement](../../schemas/VALIDATION.md). A separate annotation assembly has one owner for buf.validate; Protocol references annotations only, and the optional evaluator owns CEL dependencies. Original PGV options remain. Typed metadata translation preserves conditional oneof-member requirements while retaining Protovalidate's intentional required semantics. The bounded 33-case comparison and local package proof inform G1; full T26/GV and publication remain separate. Source adaptations and all fingerprints are recorded.

**Consequence:** Supply an explicitly invoked optional package, with no evaluator dependency or validator contract in Core. No publication decorator or automatic validation is added. Every schema update reports rule changes, unsupported constraints, and coverage. A validation package or parity claim cannot ship before its GV evidence passes. Exact Go API shape/error wording is a separate commitment.

## ADR11 — Independent provenance and compatibility

**Reason:** A Go module revision, mirrored schema revision, external import revision, and released Envoy binary are different artifacts. A schema compile is not proof that a proxy accepted a dynamic update.

**Consequence:** Pin each input and the interop images separately. Preserve the old/new diff and rerun the appropriate matrix on updates. Do not rewrite historical test evidence to a new pin.

## ADR12 — Application ownership

**Reason:** Match the upstream embedded-library scope. Persisting business configuration, trusting node IDs, and reconnect policy depend on the application.

**Consequence:** No database, Kubernetes controller, leader-election system, rate-limit decision engine, or automatic ADS retry policy enters the root scope. Show a useful host/example with explicit auth, shutdown, reconstruction, and channel ownership. Only owned calls/timers are disposed by the library.

## ADR13 — Parity is the feature ceiling

**Instruction:** Feature availability must stop at parity and avoid unnecessary complexity.

**Consequence:** Each production feature and option must map to the selected root-library matrix, its approved schema-validation constraints, or a necessary .NET adaptation. Preserve upstream defaults and limitations. Implement the direct stream/watch/cache flow first and justify extra abstractions with a concrete porting need. Automatic retries/rollback, validation-on-publication, Delta heartbeat scheduling, core quotas, and speculative ownership/performance APIs remain outside scope. T03 now informs G1; T26 has its separate validation gate. T27/T28 still require an explicit scope decision. T25 checks additions as well as missing parity.
