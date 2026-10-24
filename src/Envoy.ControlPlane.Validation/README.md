# Optional validation candidate

This experimental package builds the pinned TELUS ProtoValidate evaluator source against the separately owned annotation contracts. It does not reference the published ProtoValidate or Celly.Protovalidate packages. CEL and ordinary .NET dependencies remain centrally pinned.

Consumers explicitly construct `ProtoValidate.Validator` and call `Validate(message, failFast: false)`. Constraint violations carry field/rule information; compilation and execution faults are exceptions. Core, snapshot publication and hosting do not invoke this evaluator automatically.

T03 establishes bounded feasibility for the selected catalog. Full PGV equivalence, upstream evaluator conformance and validation-package publication remain gated by T26/GV. This candidate is not a complete validation-parity release.
