# T24 preview packaging and reproducibility

This task completes preview tooling for V01–V04/V37/V38, building on T04's package consumers, T03's original/derived metadata checks and T23's separate live-proxy proof. It does not publish packages or establish complete root-library or optional-validator parity. T12 assembles the preview acceptance record.

## Existing package and generation proof

`bindings generate` retrieves checksummed immutable archives, verifies catalog/import closure and notices, runs pinned Buf/official plugins, and rejects descriptor/generated-source/configuration drift. Protocol tests compare every selected runtime descriptor against its source, including original PGV options under the derived metadata. T24 changes no generator/source pins, transformations or generated-code fingerprints.

`repository verify` builds/tests the solution, runs the Go corpora, packs five local candidates and checks assemblies, XML documentation where supplied, readmes, license declarations and portable symbols. It now verifies the exact bytes of the twelve locked schema notices, Core's upstream license, annotation/evaluator notices and redistributed manifests. The evaluator retains its existing upstream XML-documentation limitation.

Full Protocol, Validation, Core and Hosting suites run against fresh packages without project references and with empty NuGet caches. Executable Core/ASP.NET consumers assert exact package/shared-framework graphs. Protocol tests verify descriptor ownership and required Any payloads. Core has no ASP.NET host, evaluator or CEL dependency. The optional evaluator's local package remains research evidence; publication and validation-parity claims require T26/GV.

## Update procedure

After generation, save a baseline **outside `obj/upstream`**, which generation replaces:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream report /path/to/evidence/before.json
```

After intentionally changing pins/configuration and reviewing/regenerating their locks:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache /path/to/source-cache
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream report /path/to/evidence/after.json
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream diff /path/to/evidence/before.json /path/to/evidence/after.json /path/to/evidence/update.json
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
```

Snapshots record library revision/dirty state; source/schema, generator, migration, evaluator and corpus pins; package versions and build/configuration/tool-source fingerprints; original/derived schema fingerprints; original PGV field/message/oneof rules; and derived Protovalidate field/message/oneof options. The typed schema helper verifies original descriptor invariance first. The main CLI stays independent of generated Protocol, and `upstream diff` needs no generated bindings.

Diffs retain ordered added/changed/removed entries and exact **before/after values** for inputs, files and rules. Rule-value changes remain visible when rule names are unchanged. Explicit false options and empty duration boundaries remain present. Unknown descriptor data is covered by file fingerprints; interpreting future options and establishing support still requires review.

Catalog-wide conformance is **unproven**, including recorded PGV paths, message/oneof and derived constraints. Unsupported constraints are **not assessed**, rather than an empty supported set. The bounded 33-case corpus is identified by its lock/hash; a snapshot does not claim those cases ran. Every diff has `validationAcceptance: false`. Changed inputs/rules require regeneration and the entire declared Go/.NET corpus. Affected new/changed constraints additionally need T26/GV evidence before validator acceptance. Root preview reports retain metadata and expose the gap.

Keep before/after reports with their verification artifacts. Do not overwrite historical evidence or reuse old passes for changed inputs. A metadata diff does not prove runtime or proxy behavior; run the relevant behavioral suites and the separate `envoy verify` command for the live preview graph.

## Requirement coverage

| Requirement | Evidence |
|---|---|
| Rule additions/removals/value changes | `ReproducibilityDiffTests.RulesAddedChangedAndRemoved_RetainExactOldAndNewValues` asserts all three, including false and zero-duration values. |
| Source/tool/schema/derived deltas | `SourceToolSchemaAndDerivedChanges_AreReportedIndependently` asserts each section's old/new values. `DiffCommand_WritesAnUpdateReportWithoutGeneratedBindings` exercises the actual CLI and changed schema commit. |
| No-change/incomplete reports | `SameInputsAndRules_ReportNoChangeWithoutApprovingValidation` keeps validation acceptance false; `IncompleteSnapshot_IsRejectedInsteadOfReportingNoChanges` rejects an omitted derived-rule section. |
| Real catalog and original metadata | `DescriptorSnapshotTests.RealCatalog_ReportsOriginalRulesAndDerivedOptionsWithoutConflatingThem` checks 523 original/524 derived files, 1,371 PGV entries and 59 paths; `DerivedContractDrift_IsRejectedBeforeProducingAnUpdateSnapshot` rejects a changed schema package. Existing full descriptor tests remain authoritative. |
| Packaged notice bytes | `PackageNoticeTests.PackagedNotice_MustContainTheExactLockedBytes` accepts the expected bytes and rejects replacement/missing content. `repository verify` applies these checks to actual packages and locked sources. |
| Clean graph/target ownership | Existing fresh-package suites and `tests/consumers/` executable checks; `PackageContentsVerification` inspects each net10.0 archive and symbol package. |

`repository verify` writes `obj/upstream/results/reproducibility/snapshot.json` and retains all four test-consumer locks beside their reports. Executable consumers retain locks/runtime configurations separately. Cross-platform CI uploads results, locks, metadata proof and candidate packages with its existing read-only permissions. T23's exact-head Linux workflow uploads proxy/image/traffic/admin evidence separately. Readiness requires no publishing credentials or write permissions.

Added cases bring tooling to 59, Protocol to 14 and solution discovery to 271. The implementing PR records observed local/CI results; registration and workflow configuration alone are not a pass. Full root 1.0 update coverage, T26/GV and publication remain separate.

Observed locally on macOS arm64 with SDK 10.0.400: complete `repository verify` passed **271 tests**, zero failed/skipped and zero compiler/Roslynator warnings/errors; all Go comparisons, five package/symbol pairs and fresh Protocol (14), Validation (18), Core (149), Hosting (31) suites plus executable consumers passed. The real report contains 523 original/524 derived schema fingerprints and 1,371 entries in each rule set. The CLI comparison of that complete report with itself has no deltas and still denies validator acceptance. A final Protocol rerun passed after strengthening exact derived-rule assertions. Cross-platform implementing-PR CI remains separately observed evidence. Solution pack emits the existing three non-packable example/probe notices.
