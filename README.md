# .NET control plane — architecture and implementation plan

Status: **T01 merged; T02 Buf binding candidate proved locally and awaiting review**, updated 24 October 2026. This repository is building a standalone .NET library with behavioral parity for the maintained v3 root library in go-control-plane. The Protocol candidate compiles and packs, with clean-consumer evidence. Control-plane runtime implementation, T03 validation feasibility and Envoy interoperability remain pending.

**Feature availability stops at Go parity.** Match the selected upstream behavior and defaults with the smallest suitable .NET implementation. The scope covers root-library v3 behavior and optional schema validation. Preserve upstream validation annotations from the first binding release, assess validation feasibility before freezing the bindings, and gate validation claims separately. The current proposal keeps Go's stream-loop/watch structure and stable-resource convention; matcher and broader catalogs remain deferred.

The separate upstream checkout is `../go-control-plane/`. Its reviewed commit is **67c5345367582741334e34ffc66a561b0ce32946**; the Envoy schema source is **82a4424f760b6ba3ffc9f2911389d061656f35c8**. See the [baseline](upstream-baseline.json), [source lock](schemas/upstream.lock.json), [catalog](schemas/catalog.json), and [T01 provenance evidence](schemas/PROVENANCE.md). Retrieval and inspection use a [C# file-based app](scripts/Upstream.cs).

The [T02 binding proof](schemas/BINDINGS.md) uses **Buf CLI**, pinned official C#/gRPC plugins and unchanged upstream schemas. Protocol targets **net10.0**. Its three generated C# naming corrections and exact compatibility evidence are documented there. [Bindings.cs](scripts/Bindings.cs) runs the repeatable generation, pack and clean-consumer proof; provider freeze remains gated by T03.

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

C# tooling and projects use **Roslynator.Analyzers 5.0.1**, warnings as errors, and the root [.editorconfig](.editorconfig). [Directory.Packages.props](Directory.Packages.props) centrally manages dependencies, including file-based app directives without inline versions. T04 extends these conventions to the remaining runtime projects.

[AGENTS.md](AGENTS.md) records repository-specific development and review instructions. The [command map](scripts/README.md) explains the file-based entry points and their supporting code.

The [project-based tests](tests/README.md) use xUnit/Microsoft Testing Platform and locked dependencies. Locally, **19 upstream CLI tests**, **8 binding tests** and the same **8 tests from a clean NuGet consumer** pass. [CI](.github/workflows/upstream-verification.yml) registers both projects and the package proof for Linux, macOS and Windows; T02 remote execution is pending publication.
