# Optional validation

NuGet package: `Knightfall.Envoy.ControlPlane.Validation` (explicit optional evaluator). The assembly remains `Envoy.ControlPlane.Validation`.

This optional package builds the pinned TELUS ProtoValidate evaluator source against the separately owned annotation contracts. It does not reference the published ProtoValidate or Celly.Protovalidate packages. CEL and ordinary .NET dependencies remain centrally pinned.

Consumers explicitly construct `ProtoValidate.Validator` and call `Validate(message, failFast: false)`. Constraint violations carry field/rule information; compilation and execution faults are exceptions. Core, snapshot publication and hosting do not invoke this evaluator automatically.

The evaluator rejects PGV-only validation metadata with a `CompilationException` naming the affected field, oneof or message. Regenerate those bindings through the PGV-to-Protovalidate migration before validation. This check runs when a descriptor is first used, includes nested messages, respects explicitly skipped fields, and does not unpack `Any` payloads. Our generated Envoy bindings already carry migrated annotations. Native Protovalidate messages and messages without constraints remain supported.

This is a missing-migration check, not a runtime PGV translator or a proof that paired annotations are semantically equivalent. The locked generation and Go comparison checks provide the migration evidence for the selected catalog.

Ordinary message descriptors must have a CLR parser. Metadata-only descriptor graphs throw `CompilationException` instead of returning a successful result without evaluating constraints. Evaluator caches and recursive compilation use descriptor identity, so a different descriptor with the same protobuf name cannot replace a generated message's rules or satisfy its closed-set registration.

T26 verifies the pinned catalog's 59 populated PGV rule paths and 86 path combinations using real Envoy field boundaries, whole-message cases, selected PGV fixtures and stable Protovalidate conformance. Four declared NaN range differences and seven relative-time exclusions qualify that evidence. Opaque Any payloads are not unpacked automatically. See the [T26 report](https://github.com/knightfall/envoy-dotnet-control-plane/blob/main/docs/plan/T26-VALIDATION-CONFORMANCE.md) for exact pins, coverage and acceptance results. This package is not a universal PGV replacement.

Unknown encoded Protovalidate constraints and unsupported metadata enum values fail compilation. Malformed CEL and rule/type mismatches are compilation faults; causes are retained. Registered predefined CEL extensions remain supported. These checks prevent unsupported metadata from being silently reported as valid.

Both CEL `matches` call forms use managed implementations of RE2 matching for the pinned Envoy patterns and the predefined header and UUID patterns. Other patterns retain .NET regex syntax and use a 100 ms match timeout and a 16 KiB UTF-8 pattern limit. Invalid patterns, over-limit patterns and timed-out matches fail validation with an execution exception. The timeout applies to matching. Applications that accept externally authored rules must also control rule compilation and validation concurrency. General CEL regex compatibility with RE2 remains open.

Set `new ValidatorOptions { RequireCatalogRegexPatterns = true }` when creating the validator to reject patterns outside the managed table with an execution exception. For applications that need custom patterns, `CustomRegexMatchTimeout` can lower their per-match timeout below 100 ms. Both options apply to both CEL call forms.

## Supply-chain verification

Find the matching package and its release evidence on [GitHub Releases](https://github.com/knightfall/envoy-dotnet-control-plane/releases). The package's `security/provenance.json` identifies verification locations; it is not itself an attestation. Follow the [package verification guide](https://github.com/knightfall/envoy-dotnet-control-plane/blob/main/docs/releases/VERIFYING.md) to verify the release artifact and compare it with a NuGet download.
