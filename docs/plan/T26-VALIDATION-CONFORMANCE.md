# T26 — Optional validation and conformance

Work in progress for [issue #32](https://github.com/knightfall/envoy-dotnet-control-plane/issues/32). This extends the [bounded T03 proof](../../schemas/VALIDATION.md); **GV is not yet established**. Publication is queued separately after this issue. The four root package identities and existing evaluator separation are retained.

## Catalog compilation and PGV Any presence

An audit of all **1,502 generated Protocol message classes** found 21 evaluator compilation failures. All came from the same original annotation: `ExtensionConfigSource.config_source` is a `ConfigSource` message annotated with PGV `any.required`. The pinned Go generated validator requires a non-null value but does not invoke that field's nested validator. The ordinary `ConfigSource` validator separately requires its configuration-source oneof.

Official migration added an Any-specific rule to that ordinary message, which Protovalidate correctly rejected as a type mismatch. Replacing it with ordinary required-message validation would also change behavior by validating nested contents. The controlled overlay instead marks that field `IGNORE_ALWAYS` and adds a message CEL presence rule, `has(this.config_source)`, with ID `pgv.any.required`. It retains the original PGV option. This is a derived metadata correction, without a new evaluator source patch or replacement schema.

The overlay report records the one affected field in `PresenceOnlyMessageRules`, alongside the existing sixteen conditional-oneof corrections. Removing the derived field/message options and added import leaves every original descriptor equal. Other `ConfigSource` fields keep their ordinary nested validation.

| Observation | Evidence |
|---|---|
| Reproduced original failure | Focused validation class: 8 passed, 1 failed with the Any/message rule compilation error |
| Missing/present/ADS/empty type-list behavior and independent direct-child validation | Six added fixtures in `pgv-cases.json`; all **39** declared cases match actual pinned Go PGV |
| Full selected generated-message compilation/execution | `Catalog_AllGeneratedMessages_CompileAndEvaluateWithoutFaults` checks all 1,502 classes; invalid defaults remain ordinary violations rather than evaluator faults |
| Original metadata and exact wire contracts | `Overlay_AnyRulesOnMessage_PreservePresenceWithoutNestedValidation` and the full 523-file original-descriptor equality/drift test |
| Current local package proof | `validation verify` passed **20 tests / 0 failed / 0 skipped**, all 39 Go cases and the same 20-test suite against fresh NuGet packages |

The default-message audit establishes compilation/execution behavior, not per-rule conformance. It does not treat an invalid default as a valid configuration. Original schemas, original generated fingerprints and the pinned evaluator source remain unchanged. The new derived descriptor fingerprint is `96efd79f9cf6af6a1da9cd1138060051d536930a4788806c4608a9987b267325`; the 540-file derived C# fingerprint is `a37a7b4f68e471dccce78c4c66f062d2dd241a977ec03b230e50d5678ad6fa1a`. These are recorded in the validation binding lock, separately from T25's historical evidence.

## Maintained upstream conformance

The verified evaluator archive at `88e9c909e82c569bba7d6d722dd5c6b76ba9e965` includes **35** upstream Buf conformance schemas and an exported result set with SHA-256 `61c4c5c1bdd4208977a7b62e19549fe067c0efaa83c36252dda5ef3a6d3514d2`. The maintained generation step reuses these unchanged fixtures through pinned Buf/official C# generation. Expected `wanted` results are independent inputs; stored evaluator `got` results, historical success counts and expected-failure flags are ignored. The case source, every schema/import, generation configuration and generated C# set are pinned in `schemas/validation/conformance.lock.json`. Fixtures remain under test `obj/`, use the existing annotation owner and are not shipped in runtime packages. Source licenses and reports are retained in CI.

T26 still requires applicable official conformance, the complete claimed rule/combination corpus and coverage mapping, explicit unsupported-versus-fault diagnostics, update acceptance for unknown/unevaluated rules, and final clean package/CI/review evidence. The 59 populated original rule paths, 1,244 field entries, 127 oneof options and 94 Any fields remain the inventory to reconcile. Root publication does not invoke validation, and Core/hosting acquire no evaluator/CEL dependency.

`PinnedUpstreamCases_MatchOutcomesAndStructuredFieldDiagnostics` verifies all **2,748 stable cases**, comparing outcome kinds and unordered rule-ID/full structured field-path pairs while retaining multiplicity. Seven prerecorded relative-time timestamp cases are declared exclusions, copied from the pinned upstream loader's policy with source hash and reasons. They are reported as excluded, never passed. The selected Envoy PGV inventory has no timestamp rules. These results establish neither relative-clock conformance nor exact error text/order.

The case report rejects duplicate/missing cases, changed source pins and undeclared exclusions. The CLI independently checks that a reported pass has matching actual/expected outcomes and diagnostics, then retains the report, manifest and upstream license. Generation and clean-consumer staging preserve the test-only schema closure; no NUnit, Grpc.Tools or second annotation provider is added. The conformance lock is also a structured input to upstream update reports, so a fixture-only change selects verification rather than inheriting an older result. See [fixture provenance](../../tests/Envoy.ControlPlane.Validation.Tests/Fixtures/README.md).

Local macOS arm64 `repository verify` passed **487 solution tests / 0 failed / 0 skipped**, all sixteen root Go modes / 260 cases and negative checks, all 39 PGV cases, five package/symbol pairs, fresh suites **14/21/264/109** and both executable graph consumers. The new conformance case runs in the native suite and again in the 21-test fresh optional-package consumer. Compiler/Roslynator reported zero warnings/errors; the three existing non-packable executable notices remain. Full regeneration separately preserved the original 540-file fingerprint and verified the controlled derived output.

This is a checkpoint, not completed GV: complete selected PGV rule/combination coverage, unsupported/fault distinctions and update acceptance still require work. No package has been published.

## Accepted NaN range difference

The user approved retaining Protovalidate's deliberate NaN range rejection. Actual pinned Go PGV accepts NaN because its generated invalid-range comparisons evaluate false. Upstream [PGV issue #548](https://github.com/bufbuild/protoc-gen-validate/issues/548) reports that behavior as a defect; [Protovalidate PR #81](https://github.com/bufbuild/protovalidate/pull/81) explicitly adds NaN range failures and conformance cases. The [maintainer's confirmation](https://github.com/bufbuild/protovalidate/issues/69#issuecomment-1718439062) links the completed special-value support.

The maintained Envoy corpus now contains **52 cases**: **48 equivalent outcomes** and **four declared NaN differences**, covering a double range, a strict lower bound, a float wrapper and repeated items. Both .NET and Go expectations are asserted for every case, including differences. Positive/negative infinity, finite boundaries, repeated uniqueness and normal rejection cases remain checked. The locked exception has an exact case inventory and reason, so it cannot silently absorb other differences. `NaN_RangeViolation_IdentifiesFieldAndRule` checks the structured field and rule diagnostic.

No custom floating-point overlay or evaluator patch is retained. The derived descriptor/generated fingerprints remain those recorded above. Consequently the validation claim must state this exception rather than promise exact Go acceptance for every floating-point value.

Local macOS arm64 verification for this checkpoint passed **22 validation tests / 0 failed / 0 skipped**, all 52 actual Go PGV expectations, the unchanged **2,748 stable upstream cases / seven declared exclusions**, and the same **22-test fresh optional-package consumer**. Five focused CLI guard cases also passed, proving rejection of undeclared exceptions, altered outcomes, missing reasons, changed case inventories and relabeled equivalent cases. Compiler/Roslynator reported zero warnings/errors. The two additional Go message imports require locked test-oracle transitive modules; no .NET runtime dependency changes were made. CI for the new commit is separate from these local results.

## Additional real-Envoy rule combinations

Fifteen additional cases extend the corpus to **67**. They cover repeated integer lower/upper bounds and values outside them; empty, valid and invalid optional header names; valid, empty and newline-containing repeated header names; and certificate SPKI limits in both Unicode runes and UTF-8 bytes. These use the actual pinned Envoy message types and the exact same serialized messages in the .NET and Go validators. The four previously approved NaN differences remain explicit; the other 63 cases declare equivalent outcomes.

Local macOS arm64 `validation verify` passed **24 native tests / 0 failed / 0 skipped**, all **67 actual Go expectations**, the **2,748 stable upstream conformance cases / seven declared exclusions**, and **24 fresh optional-package consumer tests**. No generated or descriptor fingerprint changed. This expands combination coverage; it does not establish complete GV coverage by itself.

## Reject unrecognized source rules

`validation verify` now checks the original catalog against [the recognized-rule manifest](../../schemas/validation/rules.recognized.json). It rejects new rule paths, unknown encoded fields in nested PGV rule messages, unsupported enum values, and enabled PGV message-level `disabled` or `ignored` options until those cases are reviewed. The checker identifies the affected message or field. It runs for optional validation verification; it does not change the source schemas or imply that every recognized rule has conformance evidence.

The manifest is included in reproducibility inputs, so a changed manifest is visible in an upstream update report. Tests cover new rule paths, unknown encoded fields, unsupported well-known-regex values, disabled message metadata and the verification error's field/rule details. Catalog-wide GV acceptance, arbitrary custom descriptors and universal Any traversal remain separate, unproven claims.

Local macOS arm64 `repository verify --coverage` passed **509 solution tests / 0 failed / 0 skipped**, all 67 Go validation comparisons, the stable upstream conformance corpus and fresh package-consumer suites of **14/30/268/109**. A generated update snapshot recorded the manifest and still reported catalog-wide conformance as false. CI and automatic review of the final PR head remain required.

## Fail-closed evaluator descriptors

The P1 review reproduction rebuilt the real `Percent` descriptor while retaining both annotation sets. Its missing CLR parser caused the evaluator to return success without compiling constraints. The name-keyed cache could then reuse that empty evaluator for an ordinary generated `Percent`, incorrectly accepting `101`.

The locked source adaptation now throws `CompilationException` for ordinary descriptors without a CLR parser. Cached evaluators and the recursive compilation map use descriptor identity, including preloaded and closed descriptor sets. This prevents same-name descriptors from changing generated-message rules. The implementation retains existing well-known-type handling and adds no dependency or public API.

`EvaluatorDescriptorTests` contains six regression cases. All six failed against the original implementation and passed after regenerating the adapted evaluator from the verified upstream archive:

| Requirement | Regression method |
|---|---|
| Unsupported metadata-only input faults before or after generated-message use; later generated rules remain effective | `Validate_MetadataOnlyDescriptor_ThrowsWithoutChangingGeneratedRules` |
| Explicit preloading cannot install an evaluator without a CLR parser | `Preload_MetadataOnlyDescriptor_ThrowsCompilationException` |
| Preloaded same-name metadata cannot strengthen or weaken the generated rules | `Preload_SameNameDescriptor_DoesNotChangeGeneratedRules` |
| A same-name descriptor cannot satisfy closed-set registration for another descriptor | `ClosedDescriptorSet_SameNameDescriptor_DoesNotRegisterGeneratedDescriptor` |

The fixtures reuse the real upstream descriptor and existing CLR message types. They generate no additional message owner. The skipped-child migration-guard test now expects the separate root-parser fault from its metadata-only test graph; actual generated skipped messages remain valid.

Local macOS arm64 regeneration and `repository verify` passed **898 solution tests / 0 failed / 0 skipped**, all 68 Go validation expectations, the 2,748 stable Protovalidate cases and 373 selected PGV cases, all sixteen root comparison groups and negative checks, five package/symbol pairs, fresh suites **14/418/268/109**, and both executable package graphs. The build reported zero compiler/Roslynator warnings or errors; packing retained the three expected non-packable-project notices. Original and derived protobuf fingerprints are unchanged. Current-head CI and automatic PR review remain separate; this fix does not complete GV.
