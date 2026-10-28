# Optional validation candidate

NuGet candidate: `Knightfall.Envoy.ControlPlane.Validation` 0.1.0-t03 (experimental evaluator). The assembly remains `Envoy.ControlPlane.Validation`.

This experimental package builds the pinned TELUS ProtoValidate evaluator source against the separately owned annotation contracts. It does not reference the published ProtoValidate or Celly.Protovalidate packages. CEL and ordinary .NET dependencies remain centrally pinned.

Consumers explicitly construct `ProtoValidate.Validator` and call `Validate(message, failFast: false)`. Constraint violations carry field/rule information; compilation and execution faults are exceptions. Core, snapshot publication and hosting do not invoke this evaluator automatically.

The evaluator rejects PGV-only validation metadata with a `CompilationException` naming the affected field, oneof or message. Regenerate those bindings through the PGV-to-Protovalidate migration before validation. This check runs when a descriptor is first used, includes nested messages, respects explicitly skipped fields, and does not unpack `Any` payloads. Our generated Envoy bindings already carry migrated annotations. Native Protovalidate messages and messages without constraints remain supported.

This is a missing-migration check, not a runtime PGV translator or a proof that paired annotations are semantically equivalent. The locked generation and Go comparison checks provide the migration evidence for the selected catalog.

Ordinary message descriptors must have a CLR parser. Metadata-only descriptor graphs throw `CompilationException` instead of returning a successful result without evaluating constraints. Evaluator caches and recursive compilation use descriptor identity, so a different descriptor with the same protobuf name cannot replace a generated message's rules or satisfy its closed-set registration.

T26 verifies the pinned catalog's 59 populated PGV rule paths and 86 path combinations using real Envoy field boundaries, whole-message cases, selected PGV fixtures and stable Protovalidate conformance. Four declared NaN range differences and seven relative-time exclusions qualify that evidence. Opaque Any payloads are not unpacked automatically. See the repository T26 report for exact pins, coverage and acceptance results. This experimental candidate is not a universal PGV replacement; publication remains a separate release step.

Unknown encoded Protovalidate constraints and unsupported metadata enum values fail compilation. Malformed CEL and rule/type mismatches are compilation faults; causes are retained. Registered predefined CEL extensions remain supported. These checks prevent unsupported metadata from being silently reported as valid.
