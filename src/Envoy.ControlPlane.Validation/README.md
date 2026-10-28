# Optional validation candidate

NuGet candidate: `Knightfall.Envoy.ControlPlane.Validation` 0.1.0-t03 (experimental evaluator). The assembly remains `Envoy.ControlPlane.Validation`.

This experimental package builds the pinned TELUS ProtoValidate evaluator source against the separately owned annotation contracts. It does not reference the published ProtoValidate or Celly.Protovalidate packages. CEL and ordinary .NET dependencies remain centrally pinned.

Consumers explicitly construct `ProtoValidate.Validator` and call `Validate(message, failFast: false)`. Constraint violations carry field/rule information; compilation and execution faults are exceptions. Core, snapshot publication and hosting do not invoke this evaluator automatically.

The evaluator rejects PGV-only validation metadata with a `CompilationException` naming the affected field, oneof or message. Regenerate those bindings through the PGV-to-Protovalidate migration before validation. This check runs when a descriptor is first used, includes nested messages, respects explicitly skipped fields, and does not unpack `Any` payloads. Our generated Envoy bindings already carry migrated annotations. Native Protovalidate messages and messages without constraints remain supported.

This is a missing-migration check, not a runtime PGV translator or a proof that paired annotations are semantically equivalent. The locked generation and Go comparison checks provide the migration evidence for the selected catalog.

T03 establishes bounded feasibility for the selected catalog. Full PGV equivalence, upstream evaluator conformance and validation-package publication remain gated by T26/GV. This candidate is not a complete validation-parity release.
