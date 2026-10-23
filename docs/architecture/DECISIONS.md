# Architecture decision register

Reviewed 24 October 2026. **Recommended** means a design default for implementation, not an approval attributed to the user. **Proof required** means the specified experiment must close before the decision is frozen. All decisions remain revisable with recorded compatibility consequences.

See [architecture](ARCHITECTURE.md), [validation](VALIDATION.md), and [implementation tasks](../plan/IMPLEMENTATION-PLAN.md).

| ID | Decision | Status | Closure / implementation |
|---|---|---|---|
| ADR01 | Target maintained root-library v3 behavior, with a SotW preview | Recommended | T01, T12, T25 |
| ADR02 | .NET 10 runtime target; Protocol target chosen by consumption proof | Recommended | T02, T04 |
| ADR03 | Protocol/Core/ASP.NET packages; optional validation adapter | Recommended | T02, T04, T26 |
| ADR04 | Exactly one binding and descriptor owner | Proof required | T02, T03; G1 |
| ADR05 | Immutable published resources with defensive copying | Recommended .NET departure | T05, T07 |
| ADR06 | One-shot watches, one event loop/writer per stream, no network awaits under cache locks | Recommended | T06, T09, T14, T22 |
| ADR07 | Preserve source defaults and mode-specific state transitions | Recommended | T08–T10, T13–T17, T21 |
| ADR08 | Treat versions/nonces as opaque; no universal Go/C# hash promise | Recommended | T05, T21 |
| ADR09 | Include unary Fetch and HTTP gateway in 1.0 | Recommended | T18 |
| ADR10 | Protovalidate for optional runtime validation, with annotation/conformance proof | Direction recommended; provider unresolved | T03, T26; GV |
| ADR11 | Separate behavior/schema/released-proxy pins and evidence | Recommended | T01, T02, T23–T25 |
| ADR12 | In-memory library with caller-owned hosting policy, storage, and channel | Recommended | T07, T11, T19 |

## ADR01 — Measurable release scope

**Reason:** The Go root module implements more than snapshot streams: client/fetch/helper APIs are part of a credible parity claim. A small preview gives early integration feedback without treating an incomplete release as full parity.

**Alternatives:** Streaming-only as permanent scope would be simpler but would require a narrower product claim. Whole-repository parity would add matcher, catalogs, and generated APIs unrelated to root runtime behavior. Those extensions have their own tasks and gates.

**Consequence:** Every matrix row maps to tasks and verification. Unsupported upstream operations remain documented. Consumers can request matcher/contrib/older target scope before package publication; no such requirement is assumed.

## ADR02 — Runtime target

**Reason:** Recommend the supported .NET 10 runtime for a new library and use its async/runtime facilities. Older runtimes add a support matrix without a named consumer in the current requirements. Confirm policy/version details at implementation kickoff against [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

**Alternative:** Multi-target Core immediately. Revisit if an actual consumer requires it. A broader Protocol target is useful only if it compiles and restores with the chosen protobuf/gRPC toolchain.

**Consequence:** T04 pins the SDK. The eventual compatibility report names targets; documentation cannot imply all .NET versions.

## ADR03 / ADR04 — Package boundaries and generated identity

**Reason:** Applications may use caches without a web host. Generated messages also serve code outside the control-plane runtime. A single authoritative message assembly prevents duplicate CLR types and descriptor registration conflicts.

**Alternatives:** A monolith couples all consumers to hosting/validation dependencies. Multiple overlapping generated catalogs cause ambiguous types. A third-party or Buf SDK is acceptable only after exact input/service/descriptor and clean-consumption proofs; availability alone is insufficient.

**Consequence:** Start with three packages and a separately installed validation adapter. If an external provider owns the messages, Protocol becomes a documented dependency facade rather than regenerating overlapping classes. T02/T03 close both message and annotation ownership before public API freeze. An incompatible provider change after publication may require a major version.

## ADR05 — Resource lifetime safety

**Reason:** Lazy bytes/hashes become invalid if a caller mutates a published protobuf object. Built-in snapshots take private copies and do not expose mutable cached instances. Go requires stable messages; the .NET API makes that requirement harder to violate.

**Alternative:** Transfer ownership by convention for all consumers. This saves allocations but makes common generated-message usage unsafe. An advanced ownership-transfer API can be considered after measured need, with an explicit lifetime contract.

**Consequence:** Include mutation-after-publication and returned-view mutation cases. Custom snapshots must uphold stability; no defensive copy claim applies to arbitrary third-party cache internals.

## ADR06 — Asynchronous watches and stream ownership

**Reason:** Porting a Go channel send under a cache lock into an awaited network send can deadlock publication. One-shot task completion decouples cache eligibility from transport delivery. Per-stream ownership limits races in nonce/subscription state.

**Alternatives:** Global response dispatcher creates unrelated-stream contention. Unbounded queues permit memory growth. Dropping arbitrary responses breaks Delta progress. Use bounded input and per-watch result slots, explicit limits, and source-equivalent draining/supersession.

**Consequence:** Immediate completion, cancellation races, blocked writers, and callback reentry are required cases. Async response/close callback faults have an explicitly documented .NET policy because the source hooks do not return errors.

## ADR07 — Fidelity before protocol improvements

**Reason:** Current defaults are unordered SotW ADS and legacy wildcard enabled. Source Delta does not implement the SotW stale-nonce branch or an ACK-committed ledger; the cache heartbeat scheduler scans SotW watches. An apparently cleaner common implementation could change these behaviors.

**Alternative:** Implement a new generalized protocol engine from the specification alone. This may be a good future extension, but cannot establish compatibility with the pinned Go baseline.

**Consequence:** Keep source/spec divergences visible in fixtures. `Snapshot.Consistent` and validation remain explicit. The unusual ADS missing-resource no-registration case needs a recorded decision before changing it. Ordered ADS remains a SotW option, not a global transaction.

## ADR08 — Version and serialization compatibility

**Reason:** Deterministic protobuf serialization is not canonical across languages. Exact SHA256 identity cannot be inferred from semantic resource equality. [Protobuf guidance](https://protobuf.dev/programming-guides/serialization-not-canonical/)

**Alternative:** Require byte/hash identity for every resource. That would need a proven canonical serialization/versioning contract beyond this library's current scope.

**Consequence:** Preserve equality/change/stale relationships in differential comparison and allow an explicit resource-version strategy. Never normalize relationships independently per message. Report literal-hash limitations in the release compatibility manifest.

## ADR09 — Fetch transport scope

**Reason:** Unary gRPC methods and eight HTTP gateway routes still exist in the reviewed source. Their shared cache/callback path is small relative to the protocol engine. The undated deprecation note does not remove their parity requirement.

**Alternative:** Defer HTTP forever; this narrows the claim and leaves existing Go consumers without equivalent behavior.

**Consequence:** Endpoint registration is opt-in. Preserve 304 and protobuf JSON behavior, LinearCache's fetch limitation, and the source RPC asymmetries for VHDS/LEDS/ratelimit configuration.

## ADR10 — Runtime validation through Protovalidate

**Reason:** Protovalidate is the successor direction for PGV. Community C# implementations provide candidates, so writing a new C# PGV generator is not the default plan. The migration and runtime proof is described in [validation](VALIDATION.md).

**Alternatives:** No validator integration would miss a useful consumer capability. Automatically validate every publication would change upstream cache behavior. Supporting both a new PGV generator and Protovalidate would add substantial unproven maintenance.

**Consequence:** Prefer one optional Protovalidate adapter behind a provider-neutral contract. Its runtime dependency stays outside Core. G1 records metadata ownership and migration feasibility; GV requires actual conformance and migrated Envoy cases. An unsupported runtime or missing annotations cannot silently pass validation. Direct Go `Validate`/`ValidateAll` API signatures are not promised.

## ADR11 — Independent provenance and compatibility

**Reason:** A Go module revision, mirrored schema revision, external import revision, and released Envoy binary are different artifacts. A schema compile is not proof that a proxy accepted a dynamic update.

**Consequence:** Pin each input and the interop images separately. Preserve the old/new diff and rerun the appropriate matrix on updates. Do not rewrite historical test evidence to a new pin.

## ADR12 — Application ownership

**Reason:** Match the upstream embedded-library scope. Persisting business configuration, trusting node IDs, and reconnect policy depend on the application.

**Consequence:** No database, Kubernetes controller, leader-election system, rate-limit decision engine, or automatic ADS retry policy enters the root scope. Show a useful host/example with explicit auth, shutdown, reconstruction, and channel ownership. Only owned calls/timers are disposed by the library.
