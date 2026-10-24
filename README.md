# .NET control plane — architecture and implementation plan

Status: **T01 source provenance implemented; G0 acceptance and runtime implementation pending**, reviewed 24 October 2026. This repository contains a proposed standalone .NET library with behavioral parity for the maintained v3 root library in go-control-plane. No runtime implementation, generated bindings, package build, or Envoy interoperability result exists yet.

**Feature availability stops at Go parity.** Match the selected upstream behavior and defaults with the smallest suitable .NET implementation. The scope covers root-library v3 behavior and optional schema validation. Preserve upstream validation annotations from the first binding release, assess validation feasibility before freezing the bindings, and gate validation claims separately. The current proposal keeps Go's stream-loop/watch structure and stable-resource convention; matcher and broader catalogs remain deferred.

The separate upstream checkout is `../go-control-plane/`. Its reviewed commit is **67c5345367582741334e34ffc66a561b0ce32946**; the Envoy schema source is **82a4424f760b6ba3ffc9f2911389d061656f35c8**. See the [baseline](upstream-baseline.json), [source lock](schemas/upstream.lock.json), [catalog](schemas/catalog.json), and [T01 provenance evidence](schemas/PROVENANCE.md). Retrieval and inspection use a [C# file-based app](scripts/Upstream.cs).

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

C# tooling uses **Roslynator.Analyzers 5.0.1** and the root [.editorconfig](.editorconfig). The file-based app treats warnings as errors. Build it with `dotnet build scripts/Upstream.cs`; T04 applies the same analyzer policy to the runtime projects when they are created.
