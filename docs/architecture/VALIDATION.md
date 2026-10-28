# Optional schema validation

Reviewed **28 October 2026**. The user selected an implementation built from pinned ProtoValidate source. [T03 evidence](../../schemas/VALIDATION.md) records separate annotation ownership, the derived metadata correction and the 33-case Go comparison. The provider arrangement is implemented for bounded local review. The [T26 report](../plan/T26-VALIDATION-CONFORMANCE.md) records the implemented declared-scope GV evidence, accepted NaN differences, exclusions and current-head acceptance gates. Optional-validator publication remains a separate step.

## 1. Compatibility boundary

Keep three commitments separate:

| Commitment | Required evidence |
|---|---|
| Schema fidelity from the first binding release | Original upstream validation options and annotation descriptors remain available, with one owner and pinned provenance |
| Optional explicit validation | Proven acceptance/rejection semantics for the selected catalog, structured field/rule diagnostics, and visible unsupported rules |
| Cache and discovery behavior | SetSnapshot and other publication APIs preserve Go behavior without automatically validating resources |

The target is the pinned upstream Go PGV validation semantics. The selected implementation uses ProtoValidate source and Protovalidate annotations; the pinned Go PGV behavior remains the compatibility contract. Exact Go Validate/ValidateAll signatures, error text, and violation ordering are not promised. A full semantic-parity claim requires all applicable rules in its declared catalog to pass GV. A package with approved exclusions must describe limited coverage rather than claim full parity.

Schema validation does not establish Snapshot.Consistent reference-set correctness or acceptance by a released Envoy proxy. Those remain independent checks.

## 2. Early binding and metadata proof — T03 / G1

T02 proposes a binding provider and catalog. Before G1 freezes that choice, T03 must:

1. Inventory every validation annotation used by the selected schemas, including nested/imported messages and payload types carried in Any. Record absent validation rules as such; do not invent new rules.
2. Verify that original options and their definitions remain accessible in the generated descriptors, without duplicate CLR types or protobuf full names.
3. Pin viable .NET runtimes and a migration tool if needed. Evaluate valid, invalid, and boundary messages for representative presence/required, oneof, nested-message, repeated/map, numeric, and string constraints actually present upstream.
4. Compare results with the pinned Go PGV implementation. Record unsupported rules, evaluator failures, and semantic differences; a runtime's own conformance results do not establish Envoy PGV equivalence.
5. Select a feasible metadata/provider approach and document the remaining full-corpus work for T26. If none is feasible, report a G1 blocker and the concrete scope/provider alternatives.

This is a bounded feasibility proof. G1 does not require the complete validation corpus or finished package. T04 and the streaming slice can proceed after the binding decision, while full conformance continues separately.

## 3. Candidate implementations and derived metadata

Candidate assessment includes pinned package/runtime versions, dependency ownership, relevant rule/CEL coverage, concurrency, failure behavior, license, and maintenance evidence.

| Candidate | Package assessed by T03 | Selection status |
|---|---|---|
| [telus-labs/protovalidate-net](https://github.com/telus-labs/protovalidate-net) | Pinned source at `88e9c909e82c569bba7d6d722dd5c6b76ba9e965` | Selected source arrangement: separate annotation owner, source adaptations and controlled metadata translation |
| [bsidio/celly](https://github.com/bsidio/celly) | Celly.Protovalidate 1.2.0 | Assessed; duplicates 24 selected CEL message names; current PGV rules are not executed |

Existing Envoy PGV annotations do not become executable by adding a Protovalidate runtime. The [official migration guide](https://protovalidate.com/migration-guides/migrate-from-protoc-gen-validate/) describes new annotations and constraint changes. Any migration must be tested against the original Go outcomes.

A controlled overlay may add derived Protovalidate options alongside original PGV options if the proof supports it. Record the input revision, tool version, every changed option, unsupported migration, and descriptor fingerprints. Preserve message/field/service identity and binary behavior. Treat added options as derived metadata, not an unchanged upstream descriptor. Keep one authoritative Envoy type assembly; do not distribute original and migrated copies of the same messages.

Reject a provider arrangement that prevents required metadata access or introduces overlapping definitions. Owning pinned generation remains an alternative if an existing SDK cannot satisfy the proof. The actual evaluator and package name are selected only after this assessment.

## 4. Optional package and explicit invocation — T26

The candidate Envoy.ControlPlane.Validation package owns the source evaluator and references the shared annotation contracts. It accepts Protocol resources through IMessage without referencing Protocol itself. Protocol references the annotation-only package; Core and ASP.NET Core do not reference the evaluator or its CEL/runtime dependencies.

Callers explicitly invoke validation before deciding whether to publish configuration. Return distinguishable outcomes:

- All applicable constraints passed for the declared coverage.
- Constraint violations, with structured field paths and rule identifiers where supported.
- Unsupported or unresolved rules, metadata, or Any types.
- Evaluator/descriptor faults.

Unsupported constraints and evaluator faults must never be reported as successful validation. Any traversal is limited to what the selected upstream validators and registered descriptors support; do not infer universal unpacking of embedded messages. T03/T26 document that behavior.

Keep cache publication, reference consistency, and validation separate. No Core validator interface, publication decorator, generic policy pipeline, or transactional batch-publication layer is introduced.

## 5. Conformance and release gate — GV

T26/GV requires:

1. Pinned schema, Go PGV, evaluator, migration, and corpus inputs.
2. Descriptor comparisons that preserve wire/message/service identity and report intentional option changes.
3. Positive, negative, and boundary comparison cases for every applicable rule class in the claimed catalog, with unsupported combinations and skips visible.
4. Applicable official evaluator conformance plus the separate Envoy/Go PGV comparison corpus; neither substitutes for the other.
5. Clean optional-package consumption, structured violation/fault outcomes, relevant concurrency cases, and proof that root-only consumers have no evaluator dependency.
6. A coverage report linking messages/fields/rules to outcomes, exclusions, and exact pins.

The streaming preview and root-runtime release do not depend on complete GV results. Original metadata preservation and T03 feasibility do gate binding freeze. The optional validation package and every validation-parity claim require GV; incomplete validation cannot be advertised as complete schema parity.

## 6. Every schema update

T24 update tooling and T26 validation evidence must report:

- Added, changed, and removed original validation rules.
- Changes in derived options, migration warnings, and unsupported constraints.
- Current coverage for the selected catalog and required Go/.NET comparison cases.
- Evaluator/tool version changes and affected conformance results.

Unknown, unsupported, or changed unevaluated rules block validation-package acceptance and affected claims for that candidate. Preserve and report those rules in a root-only update without implying validation support. Do not carry forward an old validation result under a new schema pin without evidence.

## 7. Task sequencing

T03 is active foundation work and a G1 prerequisite. T26 is active optional-package work with its own GV gate; optional means consumers can omit it, not that the task is deferred. Matcher and broader catalogs remain T27/T28 deferred scope.

The linked assessment separates bounded source/metadata/package proof from full T26/GV conformance. No official conformance or Envoy interoperability result is established by this design document.

Related: [ADR10/ADR13](DECISIONS.md), [implementation tasks](../plan/IMPLEMENTATION-PLAN.md), [requirements and gates](../plan/TRACEABILITY.md).
