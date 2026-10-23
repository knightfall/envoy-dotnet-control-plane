# .NET control plane — architecture and implementation plan

Status: **design only**, reviewed 24 October 2026. This repository contains a proposed standalone .NET library with behavioral parity for the maintained v3 root library in go-control-plane. No runtime implementation, generated bindings, package build, or Envoy interoperability result exists yet.

The separate upstream checkout is `../go-control-plane/`. Its reviewed commit is **67c5345367582741334e34ffc66a561b0ce32946**; the Envoy schema source is **82a4424f760b6ba3ffc9f2911389d061656f35c8**. See [provenance](upstream-baseline.json).

## Read in this order

1. [Proposal and release scope](PARITY-PLAN.md).
2. [Detailed architecture](docs/architecture/ARCHITECTURE.md): package boundaries, contracts, state machines, concurrency, hosting, and source limitations.
3. [Architecture decisions](docs/architecture/DECISIONS.md): rationale, alternatives, and unresolved proof gates.
4. [Protovalidate integration](docs/architecture/VALIDATION.md): community .NET candidates, annotation migration, and optional validation behavior.
5. [Implementation plan](docs/plan/IMPLEMENTATION-PLAN.md): ordered tasks, subtasks, dependencies, outputs, and acceptance criteria.
6. [Requirements and verification traceability](docs/plan/TRACEABILITY.md): release gates and named scenarios.

## Supporting evidence

- [Parity matrix](docs/plan/PARITY-MATRIX.md): exact service and library coverage.
- [Proposal review](docs/plan/REVIEW-2026-10-24.md): upstream refresh, inventory, and research.
- [Design audit](docs/plan/DESIGN-AUDIT-2026-10-24.md): document consistency and provenance checks, with execution limits.
- [Upstream architecture map](docs/codebase/ARCHITECTURE.md): source orientation; the seven documents in that directory describe the reference repository and proposed mapping.
- [Original draft](docs/plan/archive/PARITY-PLAN-2026-10-12.md): historical proposal, superseded by the current design.

Runtime packages target .NET 10 provisionally. A SotW/SnapshotCache preview precedes the complete root-library v3 scope. Protovalidate is the preferred optional validation direction; exact .NET runtime choice and Envoy annotation equivalence require the planned proofs. Package names and public signatures remain proposals until those gates close.
