# T03: source-built Protovalidate feasibility

Updated **25 October 2026**. The user selected a Protovalidate-based implementation built from pinned ProtoValidate source. The maintained path now has separate annotation contracts, derived Protocol metadata and an optional evaluator. The first 33-case comparison matches the pinned Go PGV outcomes. This is bounded T03 evidence; full T26/GV conformance and package publication remain separate gates.

The [earlier candidate assessment](validation/assessment-2026-10-25.md) and its artifacts remain historical evidence. Celly cleanup is deferred at the user's request. Neither the published ProtoValidate package nor Celly.Protovalidate is used by the maintained implementation or its CI entry points.

## Package ownership

| Package | Owns | Runtime dependencies |
|---|---|---|
| Envoy.ControlPlane.Validation.Annotations | The generated `buf.validate` rule/option/violation definitions | Google.Protobuf |
| Envoy.ControlPlane.Protocol | The original selected Envoy/import/PGV contracts, with added derived validation options | Annotations, Google.Protobuf, Grpc.Core.Api |
| Envoy.ControlPlane.Validation | Source-built TELUS ProtoValidate evaluator and its existing `ProtoValidate` API | Annotations, Cel, Microsoft.Extensions.Options and their locked dependencies |

Protocol has no evaluator or CEL dependency. The evaluator accepts `IMessage` and does not depend on Protocol. Generated Envoy types, annotation types and well-known types each have one owner. Core/cache/hosting implementation remains future work; validation is explicitly invoked, with no automatic validation on publication.

The original T03 proof used three local candidates at **0.1.0-t03**. T25 selects the `Knightfall.Envoy.ControlPlane` package family and promotes Protocol/Annotations to **1.0.0-rc.1**, retaining CLR/protobuf identities; the optional evaluator stays **0.1.0-t03**. All target **net10.0** and use central package management. The [current root report](../docs/compatibility/root-1.0.md) tracks candidate verification separately from this bounded research. The annotation and evaluator packages carry the original Apache-2.0 license and source manifest. Generated annotation types are produced by Buf, not Grpc.Tools.

## Pinned source and adaptations

[Sources lock](validation/sources.lock.json) records:

- TELUS ProtoValidate source commit `88e9c909e82c569bba7d6d722dd5c6b76ba9e965`, archive checksum, original annotation schema and license checksums.
- Every one of the 39 C# source files, with original and adapted fingerprints.
- Each exact source replacement and its required occurrence count.
- Official migration tool commit `04cd1e0187b7e570f3bb0f86a31daf1f2ad7237e` and verified archive.
- The independent corpus fingerprint, 33 cases and Go toolchain **1.26.4**.

The source retriever verifies the archive before extraction, materializes files under `obj/`, applies only the recorded replacements and verifies every output. Missing, additional or changed materialized C# files fail verification. A fresh checkout requires no sibling source repository or prepopulated cache.

The adaptations are deliberately explicit:

1. Move the upstream non-serialized `Violation.Value` partial property into the assembly that owns its generated violation type. This changes C# organization, not protobuf fields.
2. Make unavailable evaluators throw `CompilationException`. The original implementation returned a violation without a rule identifier; the project contract distinguishes descriptor/evaluator faults from constraint failures.
3. Populate the evaluator cache through its build path during explicit preloading. Otherwise `PreLoadDescriptors=true` combined with `DisableLazy=true` could never populate that cache.
4. Check descriptors at evaluator lookup for PGV metadata without corresponding Protovalidate metadata. The small authored guard throws `CompilationException` with migration guidance, follows nested descriptors except explicitly skipped fields, and leaves `Any` payloads opaque. It caches successful checks by descriptor identity and adds no Protocol dependency. This detects missing migration; it does not establish equivalence of paired rules.

A pinned `#nullable enable` materialization prefix preserves the upstream nullable context when imported files are classified as generated. Compiler warnings remain errors for all compiled source. Roslynator remains enabled for authored code and tests. Imported source retains upstream style under a narrowly scoped EditorConfig classification: an initial analyzer run found 78 upstream style diagnostics, and we avoid maintaining a broad formatting fork. This is not a Roslynator cleanliness claim for the upstream evaluator itself.

## Generation and metadata compatibility

`Bindings.cs` coordinates these phases in order:

1. Export the 523 original schemas through the existing provenance checks.
2. Retrieve the pinned evaluator source and generate the annotation-only assembly with the pinned official Buf C# plugin.
3. Generate and verify the original 540-file T02 C# baseline. This provides the original PGV types used by the typed metadata transformation.
4. Run the pinned official migration tool against derived copies. Preserve original PGV annotations.
5. Correct the known derived enum-name and whole-option assignment compilation issues; compile those copies with Buf.
6. Apply the oneof compatibility correction using typed original and migrated descriptors, verify original descriptor identity, and regenerate final C#/gRPC bindings through Buf. The generation image retains upstream source comments; the packaged descriptor image excludes source information.
7. Apply the same three C# naming corrections, now with exact fingerprints for the derived output, and verify all final outputs against [the validation binding lock](validation/bindings.lock.json).

Original sources remain unchanged. The original descriptor set has **523 files**; the derived set has **524**, adding `buf/validate/validate.proto`. After removing only the derived options and added import, every original file descriptor compares equal, including original PGV values, message/field definitions and service contracts. Original PGV inventory remains **1,244 annotated fields, 127 oneof options, 59 populated rule paths and 94 Any fields**.

Generated C# retains the upstream message and field explanations. Protocol and Annotations include XML documentation in their packages. `GeneratedDocumentation_MessageAndFieldComments_PreserveUpstreamExplanations` checks representative message and field comments through project references and fresh package consumption. Generator formatting and serialization code remain standard upstream output.

The derived set fingerprint is `7403284aec07c54c08d450dbc5ccea77f9c713e88c971c22421c37283e59e9f9`. The final 540-file C# fingerprint is `3d68c6a4dd31907c29c8c8de05136ded90ada8de0bf9c4efe118429d2ebca51d`. The separate one-file annotation fingerprint and configuration checksums are locked as well.

## The oneof compatibility correction

Protovalidate intentionally makes field-level `required` unconditional. Upstream confirmed this in [issue #228](https://github.com/bufbuild/protovalidate/issues/228#issuecomment-2254570306); the migration therefore needs to account for PGV's conditional member checks. We preserve the evaluator's ordinary required behavior.

[OverlayTransform](../tools/Envoy.ControlPlane.SchemaTools/OverlayTransform.cs) identifies original non-synthetic oneof members with PGV message/Any/duration/timestamp required rules. It clears the corresponding derived field-level requirement on **16 members**. C# generated oneof setters cannot select a null message, so this retains the relevant serialized/C# construction semantics. Content rules and whole-oneof requirements remain. Malformed Go-only objects such as a selected typed-nil oneof are not claimed to have an equivalent C# construction.

For `FileAccessLog`, omitting its optional format is valid; choosing another format is valid; choosing an invalid `log_format` still fails nested validation. The corpus proves these cases alongside required-oneof and selected-zero cases. The transformation report lists every affected field. No handwritten replacement `.proto` contract is introduced.

## Executed proof and regression mapping

The source evaluator produces binary messages from independently declared fixtures. The corpus test asserts typed results directly and serializes them once for the Go driver; evaluator faults fail the test immediately. Message ownership is checked separately in the dependency tests. The Go driver decodes those exact bytes and invokes the generated `ValidateAll` at commit `67c5345367582741334e34ffc66a561b0ce32946` through module `v1.40.1-0.20261023125212-67c534536758`. Expected outcomes are fixture data, not inferred from the .NET result. Empty corpora, missing validators, decoding failures and changed outcomes fail the driver.

| Requirement | Test / evidence |
|---|---|
| Representative presence, nested, string, numeric, enum, duration, repeated/map, skip and direct/opaque Any cases | `Corpus_ValidInvalidAndBoundaryCases_MatchIndependentPgvExpectations`; all 33 Go comparisons |
| Optional versus whole-oneof requirements | `Overlay_OptionalOneofMemberRequirement_IsConditional_OriginalPgvRemains`, `Overlay_RequiredOneofAndOrdinaryRequiredMessage_RetainRequirements` |
| All original descriptors preserved; wire-contract drift rejected | `Overlay_AllOriginalDescriptorsRemainEqual_ContractDriftIsRejected`; Protocol descriptor and serialization suites |
| Field/rule diagnostics and fail-fast behavior | `Violations_NestedInvalidMessage_IdentifyFieldAndRule`, `FailFast_MultipleViolations_ReturnsOneInsteadOfAll` |
| Null and unavailable evaluator outcomes; explicit preloading | `Validate_NullMessage_ThrowsArgumentNullException`, `ClosedDescriptorSet_UnregisteredMessage_IsCompilationFault`, `PreloadedClosedDescriptorSet_ValidRegisteredMessageSucceeds_UnknownMessageFaults` |
| Missing PGV migration rejected before evaluation; warm descriptor names cannot bypass checks | `PgvMetadataGuardTests.Validate_PgvOnlyField_ThrowsEvenAfterMigratedDescriptorWasCached`, `Validate_PgvOnlyNestedField_IdentifiesTheUnmigratedField`, `Validate_PgvOnlyRequiredOneof_IdentifiesTheOneof`, `Validate_LegacyMessageOptionWithoutMigration_IdentifiesTheMessage` |
| Skipped nested metadata and unconstrained messages remain usable | `PgvMetadataGuardTests.SkippedNestedMessage_DoesNotRequireItsRulesToBeMigrated`, `UnconstrainedMessage_RemainsValid`; existing direct/opaque Any and presence-only Go comparisons |
| Concurrent first use and warm reuse | `SharedValidator_ColdAndWarmUse_PreservesValidAndInvalidOutcomes`, a bounded smoke check with four callers and one valid/invalid message pair; not a proof of race freedom |
| Validation preserves the resource's binary and JSON forms | `Validate_ValidResource_PreservesBinaryAndJson` |
| Optional dependency boundary and single ownership | `Dependencies_ProtocolAndAnnotations_DoNotReferenceEvaluatorOrCel`, `Dependencies_MessageDefinitions_HaveSingleOwner` and Protocol catalog tests |
| Missing, extra or changed evaluator input rejected | `MaterializedSource_ExactInventoryAndContent_AreRequired`, four cases |
| Original PGV inventory edge cases | Existing three `ValidationInventoryTests` cases |
| Distribution graph and complete staged tests | Fresh-cache package consumers for Protocol and the optional validator |

The local source suite has **18 tests** and the Protocol suite has **12**. On macOS arm64, the final project and fresh-package runs both passed: 18/18 source tests, 12/12 Protocol tests, 24/24 offline regressions, and 33/33 independent Go cases, with zero failures or skips. The added source test separates message ownership from corpus evaluation; the added Protocol test checks packaged documentation. Authored-source compiler/Roslynator checks produced no findings. Tests use the existing xUnit/native MTP conventions. CI registers the same project and fresh-package proofs for Linux/macOS/Windows. Local evidence covers macOS arm64; remote execution awaits publication of this branch.

Repeated local generation hit Buf's remote-service limit during the readability revision. The final pinned Buf generation subsequently succeeded, all generated/configuration fingerprints matched, and project/package verification completed using the existing verification helpers without repeating remote generation. No alternate generator or repository verification mode was introduced.

## Reproduce

Use SDK **10.0.400** and Go **1.26.4**, then run from the checkout root with an external source cache:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings verify --cache <external-source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- validation verify
```

The first command generates and verifies locked contracts, packs Annotations and Protocol, and runs the Protocol project and fresh NuGet consumer. The second verifies materialized evaluator fingerprints, runs the source suite and Go oracle, packs the evaluator, and runs the same source tests against fresh NuGet packages. Generated code, retrieved source, descriptors and reports live under `obj/` or `bin/`.

The schema helper's `inventory` command remains available to write the complete original rule inventory; see the [command map](../tools/README.md). Earlier published-provider probes remain research artifacts; they are not part of this maintained verification path. No published validator package is required to generate, build or consume these source-built packages.

## Decision boundary and remaining GV work

The selected arrangement is owned Buf generation, a single annotation assembly and an optional source-built ProtoValidate evaluator. T03's claim is bounded feasibility for the pinned catalog and tested rule combinations. G1 can review that arrangement and its local package evidence; it does not establish complete validation parity or authorize package publication.

T26/GV still requires full claimed rule/combinations coverage, applicable official evaluator conformance, unsupported-rule detection, evaluator fault/limit cases, custom descriptors and Any behavior, diagnostics and clean-consumer evidence for the final API. Non-finite numbers, timestamp rules, regex differences, collection combinations and the full catalog are not established by this 33-case proof. Exact Go error text/order and method shapes are not promised. Live Envoy acceptance is a separate interoperability gate.

A schema/tool/source update must report original and derived rule changes and rerun affected comparisons. Locked fingerprints prevent an unevaluated change being accepted silently. Public package names/API remain experimental until the release review and GV requirements pass.
