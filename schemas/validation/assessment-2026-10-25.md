> Historical assessment before the source implementation. Its commands and gate status describe that stage. See [current evidence](../VALIDATION.md) for supported commands and results.

# T03 validation feasibility

Assessed **25 October 2026**, after T02 merged in PR #34. This is the first executable T03 assessment, linked to [issue #4](https://github.com/knightfall/envoy-dotnet-control-plane/issues/4). **T03 is not complete and G1 remains open.** Neither candidate is selected. The production Protocol project and its dependency graph are unchanged.

## Decision and blockers

Retain the T02 binding candidate and original PGV annotations while resolving these findings:

1. Both published candidates read `buf.validate`, not the original `validate.rules`. With the current bindings, each accepts all 15 invalid corpus cases that Go PGV rejects. Zero reported violations therefore does not establish PGV validation.
2. Celly's dependency owns 24 `cel.expr` message names already owned by Protocol. Its `Celly.Protobuf` assembly also shares 41 public CLR type names with Protocol, including nested and reflection types. These are different counts: the 24 count is protobuf message identity, not public API size. The current Celly package arrangement violates single ownership.
3. ProtoValidate bundles its `buf.validate` definitions with its evaluator. A directly generated metadata overlay would reference those definitions and pull the evaluator/CEL dependency graph into Protocol. That conflicts with the optional-package boundary.
4. The pinned official migration tool's output does not compile against ProtoValidate's bundled schema without corrections. Correcting compilation preserves original descriptors, but does not establish validation equivalence.
5. An exploratory source split avoided duplicate message ownership and kept evaluator dependencies out of its temporary Protocol project. It matched Go for **28/29** cases. The remaining valid case was rejected because migration changed required-field semantics inside an unselected oneof.

Do not freeze the binding provider, integrate an evaluator into Protocol, or proceed to a validation-parity claim on this evidence.

## Exact inputs

The original source catalog, Go baseline, descriptors, Buf CLI and official C#/gRPC plugin pins remain in [upstream.lock.json](../upstream.lock.json) and [bindings.lock.json](../bindings.lock.json). The assessment adds [assessment.lock.json](assessment.lock.json), two isolated NuGet dependency locks and a Go module/sum lock.

| Input | Pin / ownership |
|---|---|
| Go Envoy module | `v1.40.1-0.20261023125212-67c534536758`, from the reviewed commit `67c5345367582741334e34ffc66a561b0ce32946` |
| Go toolchain used | `1.26.4` |
| ProtoValidate | NuGet `1.0.1`; source `88e9c909e82c569bba7d6d722dd5c6b76ba9e965` |
| Celly.Protovalidate | NuGet `1.2.0`; package records source `fb0b964efc541015fe2ce534be8e0ce25395c57d` |
| Official migration source | `bufbuild/protovalidate` at `04cd1e0187b7e570f3bb0f86a31daf1f2ad7237e` |
| Migration annotation schema | Original `buf/validate/validate.proto` carried by ProtoValidate `1.0.1`; checksum locked |
| Test corpus | 29 independently declared valid/invalid/boundary cases in [cases.json](../../tests/validation/cases.json); checksum locked |

Both candidates are Apache-2.0. Their source repositories were unarchived when inspected. ProtoValidate's inspected source commit was dated 8 June 2026. Celly's inspected main commit was `c31c2774f4df7b9bbbf0786906b43b3015db8aa4`, dated 18 July 2026; this differs from its package's embedded commit. Source inspection at main is not proof that every package implementation detail matches it.

Primary candidate sources: [ProtoValidate project and dependencies](https://github.com/telus-labs/protovalidate-net/blob/88e9c909e82c569bba7d6d722dd5c6b76ba9e965/src/ProtoValidate/ProtoValidate.csproj), [Celly annotation ownership](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/src/Celly.Protovalidate/Celly.Protovalidate.csproj), [Celly CEL protobuf ownership](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/src/Celly.Protobuf/Celly.Protobuf.csproj).

## Rule inventory

The full inventory traverses all **523** source descriptors, including **1,624** message definitions and their nested types. Those totals include imported and annotation definitions, not only Envoy resource messages.

| Observation | Count |
|---|---:|
| Annotated fields | 1,244 |
| Distinct populated PGV rule paths | 59 |
| Oneof-required options | 127 |
| `Any` fields, including imported/nested messages | 94 |
| Message-level disabled/ignored options | 0 |

[rules.summary.json](rules.summary.json) records counts for every populated rule path and the full inventory fingerprint. The reproducible command writes every annotated field's file, protobuf name, original rule value and nested rule paths under `tests/validation/obj/evidence/pgv-rules.json`.

The inventory preserves explicit `false` values, zero-duration bounds, combined message/scalar constraints, repeated-item rules and map-key/value rules. For example, it distinguishes `message.required` from `uint32.gte` on the same wrapper field. There is one explicit `message.skip`, on `KeyValueAppend.entry`.

`Any` field inventory is not a promise to unpack every payload. The corpus validates `FileAccessLog` directly and also packs an invalid instance into `TypedExtensionConfig`. Go rejects the direct invalid resource but accepts the opaque payload when the enclosing Any/name requirements are satisfied. The future validator must preserve that distinction.

## Executed comparisons

The .NET programs serialize real Protocol instances to binary. The independent Go driver resolves those exact message names, decodes those bytes and invokes the pinned generated `ValidateAll`. Every Go outcome must match the separately declared fixture expectation; parse failures, missing validators and empty corpora fail the driver.

| Arrangement | Cases | Match Go | Evaluator faults | Duplicate protobuf messages |
|---|---:|---:|---:|---:|
| Published ProtoValidate, current Protocol | 29 | 14 | 0 | 0 observed |
| Published Celly.Protovalidate, current Protocol | 29 | 14 | 0 | 24 |
| Exploratory split ProtoValidate source, corrected derived metadata | 29 | 28 | 0 | 0 observed |

The first two rows are versioned, repeatable assessment commands. The last row is an explicitly limited macOS arm64 exploration, recorded in [source-prototype.json](source-prototype.json); its temporary build projects are not a supported provider or a CI gate. It separated annotation types into a temporary assembly, moved the evaluator's non-protobuf `Violation.Value` partial property to that owner, and applied the same three C# naming corrections to the migrated generated code. Imported evaluator source was not analyzed with Roslynator; repository scripts/tests retain Roslynator and warnings as errors.

The corpus covers numeric lower/upper boundaries, strings, required messages, nested invalid messages, required oneofs with a selected zero value, enum values, empty/single repeated collections, nested repeated messages, map keys, duration presence and nanosecond boundaries, and direct versus opaque Any payloads. It is a bounded corpus; it does not cover all 59 rule paths or all combinations.

The ownership probe traverses the loaded candidate dependency graph and static descriptors of generated `IMessage` types. It recognizes the T02 corrected `Descriptor` classes by property type rather than assuming a static property name. It does not inspect future application assemblies or dynamically registered custom types.

## Migration and semantic difference

The [official migration guidance](https://protovalidate.com/migration-guides/migrate-from-protoc-gen-validate/) adds derived Protovalidate annotations alongside PGV annotations and calls out changed presence semantics. This proof retains original schemas under Protocol's verified inputs and modifies only generated copies under `obj/`.

The pinned migration tool changes 321 of the 523 schema files. Its `--output` mode succeeds. Its `--write` mode was also tried in temporary storage and returned a double-close error; the repeatable proof uses `--output`.

Buf initially rejects the output against the candidate's annotation schema:

- Derived `HTTP_HEADER_NAME`/`HTTP_HEADER_VALUE` enum identifiers need the candidate schema's `KNOWN_REGEX_` prefix. The correction is confined to derived `buf.validate.field` options.
- A whole derived field-option assignment for `required: true` conflicts with another option on the same wrapper field. Assigning the `required` subfield composes those options.

[MigrationCorrections.cs](../../scripts/validation/MigrationCorrections.cs) contains those two bounded corrections. The resulting descriptor fingerprint is locked. After clearing only derived field/oneof options and their added import, **all 523 original file descriptors compare equal**, including original PGV values, message/field/service identity and other options. This proves metadata preservation, not equivalent execution of new rules.

The source prototype exposes the remaining distinction:

```json
{ "path": "/tmp/access.log" }
```

For `envoy.extensions.access_loggers.file.v3.FileAccessLog`, Go PGV accepts this configuration. The optional `access_log_format` oneof is unset. Its `log_format` member's original `message.required` rule is only evaluated when that member is selected. The derived unconditional field-required rule rejects the configuration. This must become a versioned regression case for any revised overlay/evaluator arrangement; weakening the valid fixture would hide the difference.

## Candidate behavior and remaining work

ProtoValidate exposes structured field/rule violations and distinguishes compilation/unknown-descriptor errors. Source inspection shows a concurrent evaluator cache. Celly's inspected source uses a lock around compiled-program caching and exposes compile/evaluation failures separately from violations. These are implementation observations, not concurrency proofs. The current-PGV runs exercise no executable `buf.validate` constraints. Migrated-rule concurrency, descriptor faults, unsupported-rule rejection, the upstream conformance corpus and the full claimed PGV corpus remain untested.

The published ProtoValidate restore resolves Cel, ANTLR, Microsoft.Extensions.Options and its dependencies, and TimeZoneConverter. Cel also declares System.Collections.Immutable; .NET 10's framework dependency pruning omits that package from the resolved graph. The checked NuGet lock records the executed graph. Celly resolves Celly.Protobuf, Celly and Google.Protobuf. Neither dependency graph is added to production Protocol.

The concrete next experiment is a **versioned split annotation/evaluator source build of ProtoValidate**, with a metadata correction for PGV presence inside oneofs and comparison against Go. It requires owning an upstream source adaptation until a compatible split package exists. A direct upstream packaging fix is preferable for long-term maintenance; no maintainer has been contacted. Celly additionally needs a single-owner CEL binding arrangement. Neither an unmodified published package nor a descriptor-preservation result resolves those choices.

T03.1/T03.2 have an initial candidate assessment and complete original-rule inventory. T03.3 has metadata preservation and negative provider evidence, but its representative equivalence requirement is **not passed**. T03.4/T03.5 remain open pending a viable arrangement, the oneof regression, relevant concurrency/fault cases and a reviewed provider/metadata decision. T26 must still cover every claimed rule and combination, preserve diagnostics and unsupported/fault outcomes, and prove clean optional-package consumption before GV.

## Reproduce and review

Use the pinned .NET SDK and an external source cache, as in [the command map](../../scripts/README.md). Generate Protocol with `Bindings.cs generate` first. Then run from the checkout root:

```sh
dotnet run --file scripts/Validation.cs -- assess
dotnet run --file scripts/Validation.cs -- migrate --cache <external-source-cache>
```

`assess` uses locked NuGet projects, checks the inventory/corpus fingerprints, runs both isolated packages and invokes the Go oracle. Its successful exit means the documented **negative findings were reproduced**. It explicitly reports G1 open. `migrate` checks the official source archive and candidate annotation checksums, builds the pinned Go tool, records the expected Buf rejection, applies bounded corrections and compares original descriptors. It does not execute the exploratory source variant.

The existing Protocol test project and clean package consumer now include:

| Requirement | Regression test |
|---|---|
| Full-catalog counts, nested map rules and combined wrapper/message rules | `Inventory_RealCatalog_IncludesNestedCollectionAndCombinedMessageRules` |
| Explicit false values and zero-duration rule presence | `Inventory_ExplicitFalseAndEmptyDurationBoundary_PreserveRulePresence` |
| Explicit skipped messages and Any inventory | `Inventory_AnyAndSkippedMessageRules_RemainExplicit` |

Observed on macOS arm64: **11 Protocol tests and the same 11 clean NuGet consumer tests passed, 0 failed, 0 skipped**; Buf verified all 540 original generated sources; both published candidates ran all 29 cases; Go matched all 29 declared outcomes; corrected migration preserved all 523 descriptors. Repository tooling and proof builds completed without compiler or Roslynator findings. Linux/Windows execution of the new assessment is configured in CI but has not run for this local branch. No official evaluator conformance, full validation equivalence or live Envoy acceptance is claimed.
