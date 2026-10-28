# T26 — Optional validation and conformance

[Issue #32](https://github.com/knightfall/envoy-dotnet-control-plane/issues/32) and [PR #73](https://github.com/knightfall/envoy-dotnet-control-plane/pull/73) implement explicitly invoked validation for the pinned standard Envoy catalog. This report defines the finite GV acceptance scope. Current-head CI and automatic Codex PR review must pass before merge. Package publication is a separate operation.

## Declared compatibility scope

The original catalog contains **523 files, 1,244 fields with PGV rules, 127 oneof options and 94 Any fields**. Its populated field rules use **59 paths in 86 distinct path combinations**. [The coverage manifest](../../tests/Envoy.ControlPlane.Validation.Tests/Fixtures/coverage.lock.json) binds this inventory, the derived descriptor image, rule evidence and source configuration. The generated coverage report maps all annotated fields to their combination and representative evidence fields.

The maintained proof has separate layers:

| Evidence | What it establishes |
|---|---|
| Descriptor equality after removing derived options | Original fields, services, messages, original PGV options and wire contracts remain equal across all 523 files |
| Compilation/execution audit of all 1,502 generated message classes | Each generated type can compile and execute a validator; invalid defaults remain ordinary violations |
| 68 real Envoy whole-message cases | Independently declared .NET and actual Go PGV outcomes, including required oneofs, nested messages, skipped children and four accepted NaN differences |
| 310 field-boundary cases on 90 real fields | Acceptance, rejection and boundaries for all 59 populated paths and all 86 path combinations; both validators receive the same protobuf bytes |
| 373 selected upstream PGV harness cases | Independent original PGV expectations and actual Go outcomes for rules within the selected catalog inventory |
| 2,748 stable upstream Protovalidate cases | Expected outcome kinds and unordered structured field-path/rule-ID diagnostics, including expected compilation/runtime errors |
| Fresh package consumers and package-graph checks | Validation works with the built NuGet candidates, one annotation/message owner, and no evaluator/CEL dependency in Core or hosting |

Field cases select diagnostics for the named field. Other default fields may violate unrelated constraints; these are **field outcomes, not whole-message validity claims**. Cases cover rule classes and combinations rather than every catalog parameter value or every possible input. There is no claim of universal Any traversal, arbitrary custom-schema equivalence, exact Go error text/order, newer schema revisions or broader contrib/legacy catalogs. Validation does not establish resource-reference consistency, proxy acceptance or xDS ACK/NACK behavior.

The upstream fixture selection remains explicit: all **1,053** original PGV expectations are checked by the pinned Go harness; **373** apply to the selected .NET scope and **680** are recorded as outside it. Seven prerecorded relative-time timestamp cases are excluded according to the pinned upstream loader policy and are never counted as passes. The selected Envoy catalog has no timestamp rules. See [fixture provenance and licenses](../../tests/Envoy.ControlPlane.Validation.Tests/Fixtures/README.md).

## Runtime and failure contract

The optional `Knightfall.Envoy.ControlPlane.Validation` candidate builds verified TELUS ProtoValidate source at `88e9c909e82c569bba7d6d722dd5c6b76ba9e965`. The source archive, 39 source files, exact adaptations, CEL dependencies, Go toolchain and comparison cases are pinned. The separate annotation package owns Buf validation types; Protocol owns Envoy/PGV messages. No published ProtoValidate or Celly.Protovalidate runtime is consumed.

Callers explicitly construct `ProtoValidate.Validator` and call `Validate(message, failFast: false)`. Success and violations are distinct from `CompilationException` and execution faults. Field/rule diagnostics are retained. Snapshot publication and hosting do not invoke the evaluator.

The small runtime guards reject PGV-only metadata that was not migrated, unrecognized encoded Protovalidate constraints, unsupported metadata enum values and ordinary descriptors without CLR parsers. Registered upstream predefined CEL extensions remain supported. Rule/type mismatch and malformed CEL are compilation failures; unexpected compilation causes are retained as inner exceptions. Unknown opaque Any payloads remain opaque, matching the generated Go validator boundary: presence/type constraints can apply to the envelope, without implicit payload decoding. Explicit registry lookup returns no decoded message for an unregistered type; malformed bytes for a registered type fail decoding.

The P1 review defect is fixed: evaluator caches and recursive compilation maps key by descriptor identity, so metadata with the same protobuf name cannot poison generated-message validation or satisfy another descriptor's closed-set registration. Six regressions failed before that correction and passed afterward. Relevant tests are `EvaluatorDescriptorTests`, `ValidationFaultTests`, the native conformance test and the catalog audit.

## Controlled metadata corrections

Official pinned PGV-to-Protovalidate migration remains the base. Three narrow corrections preserve actual Go behavior:

| Correction | Affected locations | Behavior |
|---|---:|---|
| Conditional oneof member requirements | 16 | A message member's PGV requirement applies when selected; migration must not require selecting an optional oneof member |
| `any.required` on `ExtensionConfigSource.config_source` | 1 | Require presence without invoking the ordinary nested `ConfigSource` validator, as the actual Go Any branch does |
| Non-strict HTTP header names with no positive minimum length | 28 | Accept empty values as Go PGV does, while retaining nonempty validation and enclosing collection constraints |

The last correction uses derived `IGNORE_IF_ZERO_VALUE` only for the supported original rule combinations; new combinations fail generation until reviewed. It also covers repeated items. Positive minimum lengths remain enforced. `OverlayTests` checks these boundaries and verifies original descriptor equality.

Original descriptor SHA-256 remains `c34501434b0580470167d4a257269e25d921dbd5c6ba4968255642cf1e3d10a4`. The derived descriptor SHA-256 is `7d725cc4b71d1a5b9025fd48cc20690d0280acbbf5544fd13537345f605be06e`; its 540-file generated C# fingerprint is `42727efcd5b2e21267af63639ca0e2227c6a188f40454e218b306f2f42b4d6df`. The original generation lock is unchanged. Derived fingerprints, correction counts and configuration hashes are recorded separately in [the validation binding lock](../../schemas/validation/bindings.lock.json).

The sole accepted observed outcome difference is **NaN range rejection**. Four named cases require Protovalidate rejection and Go PGV acceptance. Both outcomes run and are asserted; neither is skipped or normalized. The source lock records their reason and exact IDs. This qualifies compatibility; it does not reject NaN in unconstrained fields. The earlier [T03 feasibility report](../../schemas/VALIDATION.md) and [T25 root candidate report](../compatibility/root-1.0.md) remain historical evidence for their original commits.

## Update acceptance and verification

`validation verify` and `repository verify` check recognized original PGV metadata, materialized evaluator fingerprints, all declared corpora and coverage inputs. The recognized-rule manifest is an allowlist, not a substitute for conformance. Unknown wire fields, rule paths and unsupported enum/message options are rejected with their location. The field report must contain every declared case exactly once, with unchanged expectations, matching outcomes and field/rule diagnostics; faults cannot count as expected rejection. Every catalog combination needs matching representative evidence.

Only successful comparisons produce `obj/upstream/results/validation/coverage.json`. That report is bound to the descriptors, locks, authored evaluator, fixtures, tests and verification code. `upstream report` uses it only while those inputs match; missing/stale evidence reports unproven coverage. `upstream diff` retains exact added/changed/removed original and derived rules and conservatively selects verification. A diff never grants validation acceptance or authorizes publication. Negative tests cover missing cases/combinations, duplicate IDs, changed expectations/results, fault-as-rejection, missing diagnostics, unknown paths and stale descriptor/source evidence.

Run after pinned binding generation:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- validation verify
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
```

The complete repository command additionally runs the root Go corpora, builds and inspects five package/symbol pairs, repeats four suites against fresh package-only consumers and verifies the Core/hosting dependency graphs. CI runs the same checks on Linux, macOS and Windows, with Dorny test reports and Linux portable coverage. The performance baseline stays manual. Released-Envoy verification remains a separate proof level.

Local and current-head CI results are recorded below only after completion.
