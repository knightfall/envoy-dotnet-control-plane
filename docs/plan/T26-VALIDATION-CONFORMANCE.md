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

## Remaining conformance work

The verified evaluator archive at `88e9c909e82c569bba7d6d722dd5c6b76ba9e965` includes **35** upstream Buf conformance schemas and an exported result set with SHA-256 `61c4c5c1bdd4208977a7b62e19549fe067c0efaa83c36252dda5ef3a6d3514d2`. The investigation reuses those unchanged fixtures through pinned Buf/official C# generation. Expected `wanted` results are independent inputs; stored evaluator `got` results and historical success counts must not become assertions. No current conformance pass is claimed yet.

T26 still requires applicable official conformance, the complete claimed rule/combination corpus and coverage mapping, explicit unsupported-versus-fault diagnostics, update acceptance for unknown/unevaluated rules, and final clean package/CI/review evidence. The 59 populated original rule paths, 1,244 field entries, 127 oneof options and 94 Any fields remain the inventory to reconcile. Root publication does not invoke validation, and Core/hosting acquire no evaluator/CEL dependency.
