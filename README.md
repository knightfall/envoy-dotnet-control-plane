# .NET control plane — architecture and implementation plan

Status: **T01–T11 and tooling migration merged; preview resource helpers merged; preview differential harness implemented**, updated 26 October 2026. This repository is building a standalone .NET library with behavioral parity for the maintained v3 root library in go-control-plane. The [T03 evidence](schemas/VALIDATION.md) describes separate annotation ownership, a pinned evaluator source build and 33 Go PGV comparison cases. The bounded source arrangement is recorded in merged PR #35; full optional-validator conformance remains T26/GV work. [T05](docs/plan/T05-RESOURCES-AND-SNAPSHOTS.md) adds resource entries, partial snapshots, explicit EDS/RDS consistency checks and lazy Delta versions. [T06](docs/plan/T06-WATCHES.md) adds one-shot watch completion/cancellation and custom watcher/response contracts. [T07](docs/plan/T07-SNAPSHOT-CACHE.md) adds the SotW SnapshotCache, node hashing, live status and lazy normal responses. [T08](docs/plan/T08-SOTW-SUBSCRIPTIONS.md) adds source-equivalent SotW subscriptions and legacy-wildcard settings. [T09](docs/plan/T09-SOTW-STREAM-ENGINE.md) adds the transport-independent SotW loop and callbacks. [T10](docs/plan/T10-ORDERED-ADS.md) adds channel-based SotW delivery and optional ordered ADS independently of cache readiness. [T11](docs/plan/T11-ASP-NET-HOST.md) adds the nine preview SotW service adapters, ordinary hosting registration/endpoint APIs and actual HTTP/2 tests. Delta cache delivery, Fetch, heartbeat scheduling and live Envoy interoperability remain pending.

**Feature availability stops at Go parity.** Match the selected upstream behavior and defaults with the smallest suitable .NET implementation. The scope covers root-library v3 behavior and optional schema validation. Preserve upstream validation annotations from the first binding release, assess validation feasibility before freezing the bindings, and gate validation claims separately. Use idiomatic C# APIs and coordination to preserve observable behavior, rather than copying Go's implementation structure. The stable-resource convention remains; matcher and broader catalogs remain deferred.

The remote behavior reference is recorded in [the baseline](upstream-baseline.json). Its reviewed commit is **67c5345367582741334e34ffc66a561b0ce32946**; the Envoy schema source is **82a4424f760b6ba3ffc9f2911389d061656f35c8**. See the [baseline](upstream-baseline.json), [source lock](schemas/upstream.lock.json), [catalog](schemas/catalog.json), and [T01 provenance evidence](schemas/PROVENANCE.md). Retrieval and inspection use the [project-based repository CLI](tools/README.md).

The [T02 binding proof](schemas/BINDINGS.md) uses **Buf CLI**, pinned official C#/gRPC plugins and unchanged upstream schemas. T03 adds an explicit derived validation-metadata layer while retaining original PGV options. Protocol targets **net10.0**. Its three generated C# naming corrections and exact compatibility evidence are documented there. The `bindings` command runs the repeatable generation, pack and clean-consumer proof. The `validation` command verifies the optional source evaluator and Go comparisons. The [command map](tools/README.md) explains the generation phases and pins.

## Read in this order

1. [Proposal and release scope](PARITY-PLAN.md).
2. [Detailed architecture](docs/architecture/ARCHITECTURE.md): package boundaries, contracts, state machines, concurrency, hosting, and source limitations.
3. [Architecture decisions](docs/architecture/DECISIONS.md): rationale, alternatives, and unresolved proof gates.
4. [Optional schema validation](docs/architecture/VALIDATION.md): early binding/metadata proof, candidate assessment, rule equivalence, and the separate validation gate.
5. [Implementation plan](docs/plan/IMPLEMENTATION-PLAN.md): ordered tasks, subtasks, dependencies, outputs, and acceptance criteria.
6. [Requirements and verification traceability](docs/plan/TRACEABILITY.md): release gates and named scenarios.

## Supporting evidence

- [Parity matrix](docs/plan/PARITY-MATRIX.md): exact service and library coverage.
- [T21 preview differential harness](docs/plan/T21-PREVIEW-DIFFERENTIAL.md): actual Go observations, relationship-preserving nonce comparison and injected-mismatch proof.
- [T20 preview helpers](docs/plan/T20-PREVIEW-HELPERS.md): source-to-C# mapping, narrow extension labels, typed HTTP graph and helper comparison.
- [T11 ASP.NET Core host](docs/plan/T11-ASP-NET-HOST.md): generated service adapters, host ownership, authorization, actual HTTP/2 and clean package-consumer proof.
- [T10 ordered ADS](docs/plan/T10-ORDERED-ADS.md): channel delivery, asynchronous publication, independent readiness/order and upstream comparison evidence.
- [T09 stream engine](docs/plan/T09-SOTW-STREAM-ENGINE.md): callback/nonce behavior, ownership, cleanup and independent Go stream comparisons.
- [T09 investigation](docs/plan/T09-SOTW-STREAM-INVESTIGATION.md): source findings, xDS package boundaries and the accepted transport direction.
- [gRPC/Kestrel transport validation](docs/plan/GRPC-KESTREL-VALIDATION.md): functional comparison, controlled Linux performance workflow and transport-setting differences.
- [Proposal review](docs/plan/REVIEW-2026-10-24.md): upstream refresh, inventory, and research.
- [Design audit](docs/plan/DESIGN-AUDIT-2026-10-24.md): document consistency and provenance checks, with execution limits.
- [Upstream architecture map](docs/codebase/ARCHITECTURE.md): source orientation; the seven documents in that directory describe the reference repository and proposed mapping.
- [Original draft](docs/plan/archive/PARITY-PLAN-2026-10-12.md): historical proposal, superseded by the current design.

Runtime packages target .NET 10 provisionally. A SotW/SnapshotCache preview precedes the complete root-library v3 scope. T03 is an early binding-design task; T26 delivers the optional validation package after its own conformance gate. Full validation does not block the streaming preview or root-runtime claim. T27/T28 remain deferred. Package names and public signatures remain proposals until their binding and behavioral proofs pass.

C# tooling and projects use **Roslynator.Analyzers 5.0.1**, warnings as errors, and the root [.editorconfig](.editorconfig). [Directory.Packages.props](Directory.Packages.props) centrally manages dependencies, including the two tooling projects. [T04](docs/plan/T04-BUILD-FOUNDATION.md) applies shared build/package policy and adds the Core/ASP.NET foundation projects.

[AGENTS.md](AGENTS.md) records repository-specific development and review instructions. The [command map](tools/README.md) explains the project-based commands and their supporting code.

[Envoy.ControlPlane.slnx](Envoy.ControlPlane.slnx) contains all fourteen maintained projects. The [project-based tests](tests/README.md) use xUnit/Microsoft Testing Platform and locked dependencies. T02's merged CI passed 24 offline CLI/fingerprint tests, eight binding tests and eight clean NuGet consumer tests on Linux/macOS/Windows. T03 expands Protocol to **12 tests** and adds an **18-test source validation suite** plus **33 Go comparison cases**. T04 discovers all **54 tests** through the solution, packages all five libraries with symbols, and adds executable Core/ASP.NET package-graph consumers. T05 added **38 Core tests** and **19 independent Go snapshot comparisons**. T06 added **10 watch/contract cases**. T07 added **21 native cases** and a **22-trace Go SnapshotCache comparison corpus**. T08 adds **12 native cases** and **14 Go subscription traces**, bringing Core to **81** and solution discovery to **135 tests**; the full Core suite runs in a fresh package consumer. [CI](.github/workflows/upstream-verification.yml) runs generation and complete repository verification on Linux/macOS/Windows; implementing-PR results distinguish remote evidence from local proof. Full T26/GV and live Envoy acceptance are separate evidence.

T09 adds 37 Core stream/callback cases and six independent Go traces, bringing Core to **118** and solution discovery to **173** tests. The full Core suite also runs in the fresh package consumer. These are core-loop checks; service adapters and actual xDS HTTP/2 acceptance remain T11/T12/T22.

T10 adds **26 native cases**, bringing Core to **144** and solution discovery to **199** tests. The Go stream corpus expands to **16 traces**, including ordered variants, buffered producer order, replacement draining and response-channel closure. Both ordinary and ordered streams consume channels directly; a task convenience adapter serves await-one callers. See [T10 evidence](docs/plan/T10-ORDERED-ADS.md).

T11 adds **23 hosting tests**, bringing solution discovery to **222 tests**. The same 23 tests run against freshly packed NuGet packages with real ephemeral-loopback HTTP/2. [The example](examples/Envoy.ControlPlane.Example/README.md) supplies an application configuration/update source. Only the nine advertised SotW methods are implemented; generated Delta/Fetch defaults remain unimplemented.

The preview subset of T20 adds the complete listener/HCM/router/route/cluster/endpoint example graph using existing helpers and generated bindings. Five added native cases bring the solution to **227 tests** (Core **147**, Hosting **25**). Eight declared HCM/Any cases plus labels/API version and the example graph are compared with the actual pinned Go helpers. Full message/Struct conversion and remaining helper inventory stay in the 1.0 task. See [preview helper evidence](docs/plan/T20-PREVIEW-HELPERS.md).

The preview subset of T21 expands the Go cache corpus to **23** and stream corpus to **22** traces, capturing callback rejection and missing ADS type behavior in both modes. A stream-wide nonce mapping preserves ACK, send and cross-type uniqueness relationships while keeping caller versions exact. Seventeen offline comparer cases bring tooling to **42** and the solution to **244 tests**. See [preview differential evidence](docs/plan/T21-PREVIEW-DIFFERENTIAL.md) for exact test mapping and separate native/source/proxy proof.
