# Design deliverable audit — 24 October 2026

Scope: consistency and provenance of the architecture and implementation-plan documents. This is not runtime verification or release-gate evidence.

## Observed checks

| Check | Result |
|---|---|
| Active Markdown documents, excluding the historical archive | 17 |
| Relative/local Markdown links resolved | 79; no missing target |
| Task IDs | 28 unique tasks; 26 active and 2 deferred (T27/T28) |
| Subtask IDs | 106 unique subtasks; at least three per task |
| Task dependency references | 50 edges; all defined, graph acyclic; active tasks exclude T27/T28 from their dependency closure |
| Task structure | Every task has scope, dependencies, outputs, subtasks, acceptance |
| Requirements | 26; each maps to defined tasks, verification families and a gate |
| Verification families | 40 defined; referenced IDs resolve |
| Task coverage | Every task appears in the requirement mapping |
| Mermaid diagrams | All 3 active diagrams parse with Mermaid 11.12.0 |
| Gate separation | T03 precedes T04; T26 is outside preview/root completion dependency closure |
| Source map | Exactly seven codebase documents retained |
| Upstream working tree | Clean |
| Go baseline | `67c5345367582741334e34ffc66a561b0ce32946`; HEAD equals fetched origin/main |
| Envoy schema pin | `82a4424f760b6ba3ffc9f2911389d061656f35c8`; matches envoy/COMMIT |

Checks used a one-off Python document/dependency audit and Git/source reads. Manual review covered scope, task/gate sequencing, source-specific SotW/Delta differences, optional validation dependencies, and separation of proposed work from evidence. Dependency fields are authoritative; diagrams summarize them. The matching remote-tracking ref is the previously fetched ref, not a new assertion that the remote cannot have advanced.

## Deliverable status

Architecture, decisions, optional validation design, tasks/subtasks, dependencies, acceptance criteria, and gates are documented. The user approved optional validation and early feasibility in this revision. T03 gates G1; T26 is active optional-package work behind GV. Provider/API choices remain proposed; no implementation approval or result is inferred from the scope decision.

Protovalidate primary documentation and two community .NET runtime repositories were inspected. Their availability was established; restore, execution, conformance claims, and suitability for migrated Envoy annotations were not independently validated.

No library implementation, generated binding build, package restore/pack, Go/.NET runtime tests, performance measurements, or live Envoy interoperability run was performed for this design deliverable. The implementation gates remain planned. The previous proposal is committed on `docs/parity-plan` and published as PR #1; the current parity-scope revision is local and uncommitted for user review. No package has been published.

## Earlier parity-scope revision checks

At the earlier simplification stage, re-ran the document/dependency audit and `git diff --check`. Checked that the active task dependency closure excludes T03/T26–T28, root service/cache matrix rows retain their previous coverage, and all active Mermaid diagrams parse with Mermaid 11.12.0. Confirmed the source pins and clean Go reference checkout. These checks establish document consistency; they do not establish an implemented or interoperable .NET library.

That earlier revision removed mandatory copying, extra synchronization layers, queue/watch quotas, and generic validation/publication machinery from the proposal. Race protection, watch cancellation, callback order, and source-equivalent backpressure remain correctness requirements. Extra internal machinery must address a demonstrated porting need; feature additions require a separate scope decision.

## Optional-validation scope revision

The user approved optional schema validation with early feasibility and a separate conformance gate. The architecture, ADR10/ADR13, parity matrix, tasks, requirements, and source-map guidance now agree on that boundary.

- Preserve original validation options from the first binding release.
- Require bounded T03 metadata/descriptor and representative Go/.NET results before G1 freezes bindings.
- Complete T26/GV separately; full validation does not block the streaming preview or root-runtime claim.
- Keep the evaluator in an optional package, with no Core dependency or automatic validation-on-publication.
- Require every schema update to report rule changes, derived metadata, unsupported constraints, and coverage.

Re-ran the document/dependency audit and whitespace check, parsed all 3 active diagrams with Mermaid 11.12.0, and checked task dependency closure. T03 gates T04, T26 stays outside preview/root completion, and no active task depends on T27/T28. The root service/cache matrix rows match the committed baseline. Source pins remain unchanged. These checks establish document consistency only; no validator restore, rule execution, or conformance run was performed. Changes remain local and uncommitted for review.
