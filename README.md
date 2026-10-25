# .NET control plane — architecture and implementation plan

Status: **T01–T05 and tooling migration merged; T06 watch primitives implemented**, updated 25 October 2026. This repository is building a standalone .NET library with behavioral parity for the maintained v3 root library in go-control-plane. The [T03 evidence](schemas/VALIDATION.md) describes separate annotation ownership, a pinned evaluator source build and 33 Go PGV comparison cases. The bounded source arrangement is recorded in merged PR #35; full optional-validator conformance remains T26/GV work. [T05](docs/plan/T05-RESOURCES-AND-SNAPSHOTS.md) adds resource entries, partial snapshots, explicit EDS/RDS consistency checks and lazy Delta versions. [T06](docs/plan/T06-WATCHES.md) adds one-shot watch completion/cancellation and custom watcher/response contracts. Caches, streaming services and live Envoy interoperability remain pending.

**Feature availability stops at Go parity.** Match the selected upstream behavior and defaults with the smallest suitable .NET implementation. The scope covers root-library v3 behavior and optional schema validation. Preserve upstream validation annotations from the first binding release, assess validation feasibility before freezing the bindings, and gate validation claims separately. The current proposal keeps Go's stream-loop/watch structure and stable-resource convention; matcher and broader catalogs remain deferred.

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
- [Proposal review](docs/plan/REVIEW-2026-10-24.md): upstream refresh, inventory, and research.
- [Design audit](docs/plan/DESIGN-AUDIT-2026-10-24.md): document consistency and provenance checks, with execution limits.
- [Upstream architecture map](docs/codebase/ARCHITECTURE.md): source orientation; the seven documents in that directory describe the reference repository and proposed mapping.
- [Original draft](docs/plan/archive/PARITY-PLAN-2026-10-12.md): historical proposal, superseded by the current design.

Runtime packages target .NET 10 provisionally. A SotW/SnapshotCache preview precedes the complete root-library v3 scope. T03 is an early binding-design task; T26 delivers the optional validation package after its own conformance gate. Full validation does not block the streaming preview or root-runtime claim. T27/T28 remain deferred. Package names and public signatures remain proposals until their binding and behavioral proofs pass.

C# tooling and projects use **Roslynator.Analyzers 5.0.1**, warnings as errors, and the root [.editorconfig](.editorconfig). [Directory.Packages.props](Directory.Packages.props) centrally manages dependencies, including the two tooling projects. [T04](docs/plan/T04-BUILD-FOUNDATION.md) applies shared build/package policy and adds the Core/ASP.NET foundation projects.

[AGENTS.md](AGENTS.md) records repository-specific development and review instructions. The [command map](tools/README.md) explains the project-based commands and their supporting code.

[Envoy.ControlPlane.slnx](Envoy.ControlPlane.slnx) contains all eleven maintained projects. The [project-based tests](tests/README.md) use xUnit/Microsoft Testing Platform and locked dependencies. T02's merged CI passed 24 offline CLI/fingerprint tests, eight binding tests and eight clean NuGet consumer tests on Linux/macOS/Windows. T03 expands Protocol to **12 tests** and adds an **18-test source validation suite** plus **33 Go comparison cases**. T04 discovers all **54 tests** through the solution, packages all five libraries with symbols, and adds executable Core/ASP.NET package-graph consumers. T05 added **38 Core tests** and **19 independent Go snapshot comparisons**. T06 adds **10 watch/contract cases**, bringing Core to **48** and solution discovery to **102 tests**; the full Core suite runs in a fresh package consumer. [CI](.github/workflows/upstream-verification.yml) runs generation and complete repository verification on Linux/macOS/Windows; implementing-PR results distinguish remote evidence from local proof. Full T26/GV and live Envoy acceptance are separate evidence.
