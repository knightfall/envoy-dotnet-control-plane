# T24 — Root package and upstream-update readiness

Completes the remaining tooling scope in [issue #30](https://github.com/knightfall/envoy-dotnet-control-plane/issues/30). It reuses [preview packaging](T24-PREVIEW-READINESS.md), the completed root differential/lifecycle commands and [released-proxy harness](T23-ROOT-ENVOY.md). No runtime APIs, package dependencies, schema transformations or generated fingerprints change. Preparation does not publish packages or approve the optional validator.

## Root package proof

Repository verify builds and packs all five candidates, verifies assemblies/portable symbols/XML docs where supplied, exact locked license/notice bytes, readmes and redistributed schema manifests. The evaluator retains its upstream XML-documentation limitation. Core and hosting suites now exercise their complete implemented root surface again against fresh NuGet caches with no project references. Separate executable Core and ASP.NET consumers inspect exact package and framework graphs: Core has neither ASP.NET hosting nor evaluator/CEL dependencies; hosting owns its normal shared-framework reference. Optional evaluator smoke/consumer tests remain bounded T03 research, not GV release acceptance.

Generation verifies original and derived descriptors, original PGV options, catalog/import closure, official Buf/plugin/configuration pins and generated output fingerprints. The existing Linux/macOS/Windows workflows retain packages, reports, locks, descriptor proofs, lifecycle and source comparison artifacts. The separate Linux amd64/arm64 proxy workflow records image/binary/library/source/schema pins with actual traffic/admin state. Existing read-only CI permissions are sufficient; no publishing secret or write permission is added.

## Recorded input change and selected verification

The earlier reproducibility snapshot recorded lock/build inputs and tooling sources but missed runtime/test/example source changes. A source-only change could therefore produce hasChanges=false even though the implementation or comparison corpus changed. ReproducibilityReport now records normalized SHA-256 values for authored C#/project/project-template/MSBuild props/targets/proto/JSON/Go/module/sum/YAML inputs under src, tests, tools, examples and .github/workflows. It excludes bin and obj outputs, normalizes LF and forward-slash paths before ordinal ordering, and retains explicit structured source/tool/image locks. Root NuGet/editor/line-ending configuration is recorded too. Generated/materialized sources remain represented by their authoritative existing locks, not by build-directory enumeration.

The snapshot includes the current sixteen Go/.NET corpus names from RootDifferentialVerification's actual input inventory. This is a catalog, not a claim that those cases ran. Original and derived rule additions/removals/value changes, before/after pins and coverage/unsupported fields remain intact. Existing reports are not rewritten.

Upstream diff now emits verificationSelection. If any recorded input or descriptor/rule changes, it selects every current case in all sixteen root corpora, the complete native suite and fresh consumers, binding regeneration and the pinned released-proxy run. Selection is deliberately conservative: no dependency graph, per-case pruning or invented precision is added. The selected root groups include snapshots/cache, both subscriptions/streams, helpers, Linear/Mux, heartbeats, Fetch/shared Fetch, gateway, client and conversions. Their complete native/Go cases and helper-symbol checks run through repository verify, including new cases introduced by the update. Removed cases remain visible in their prior source/report evidence; the tool does not relabel old passes as current.

With no recorded changes the selection is empty; historical results still belong to their original revision and platform. Every plan states that no command execution or passing outcome is implied. The performance baseline remains manual and is never selected. Validation acceptance stays false while GV is incomplete, even for root-only changes. Unknown or unevaluated constraints remain exposed as unproven/not-assessed, never an empty supported set.

## Reproduction

1. After verified generation, save an upstream report outside obj/upstream, which generation replaces.
2. Make an intentional source/schema/tool update, review its locks, regenerate, and save the after report.
3. Run upstream diff with the saved before/after paths and retain its exact old/new values and selection.
4. Run every selected command; keep its execution evidence separately. In particular, run repository verify and envoy verify for the current exact head. The report does not run or approve those commands itself.

[The maintained command map](../../tools/README.md) and [preview update procedure](T24-PREVIEW-READINESS.md#update-procedure) contain the complete CLI forms. Root compatibility/release review remains T25, and optional-validator acceptance remains T26/GV.

## Requirement coverage

| Requirement | Evidence |
|---|---|
| T24.1 / V01–V04: clean root graph/packaging | PackageContentsVerification and exact-notice tests; full fresh Protocol/Validation/Core/Hosting suites; RootConsumerVerification and ConsumerChecks inspect actual NuGet/runtime graphs |
| T24.2 / V37–V38: source/catalog/options/generation/platform drift | Existing binding/overlay/descriptor identity tests, immutable source/tool locks and cross-platform Buf workflow; new native Linux arm64 tool pin was verified in T23 |
| T24.3: implementation/corpus/workflow drift | New ReproducibilityInputTests.MaintainedInputs_RecordExactNormalizedHashesAndExcludeBuildOutputs checks all maintained groups, exact hashes, LF equivalence, actual source changes, structured locks, build-output exclusions and normalized path order |
| T24.3 / V37–V38: selected root cases and no false approval | New ReproducibilityDiffTests.RuntimeAndCorpusChanges_SelectFullCurrentReplayWithoutApprovingValidation checks exact old/new input hashes, all sixteen independently declared groups, commands, fresh consumers, manual-only performance, execution disclaimer and preserved validation gap; extended no-change test selects nothing |
| T24.3: original/derived rules and unsupported coverage | Existing RulesAddedChangedAndRemoved_RetainExactOldAndNewValues, SourceToolSchemaAndDerivedChanges_AreReportedIndependently, real catalog inventory/identity tests and actual CLI diff test remain unchanged |
| T24.4: retained artifacts and reproduction | Existing three-platform workflows plus both proxy architectures, repository results/packages/locks and documented before/after procedure; no publication credentials |

## Observed results and limits

Observed macOS arm64 full repository verify passes 484 solution cases (Core 264, Hosting 109, Protocol 14, optional Validation 18, tooling/provenance 79), zero failed/skipped and zero compiler/Roslynator warnings/errors. All 16 source modes/260 cases, semantic corruption checks, five package/symbol pairs, fresh consumers (14/18/264/109) and both executable package graphs pass; three existing non-packable notices remain. The real snapshot contains 374 input entries, all 1,371 original PGV rule entries and sixteen root modes. Actual CLI self-diff selects no runs; a simulated runtime-input change selects all sixteen groups/three commands while preserving validator denial. Historical before/after artifacts remain outside obj. A focused follow-up also passes for nested MSBuild props/targets, including src/Directory.Build.props that controls package versions; the refreshed real snapshot records both nested build files. The native PR review also identified the executable .csproj.template inputs; both are now included, their content changes are asserted directly, and all ten focused update-report tests pass. Exact-head CI/native PR review are recorded separately in the implementing PR. Source/schema/SDK/Go/Buf/plugin versions remain pinned. T26/GV is deliberately incomplete and cannot be passed by this update report. No NuGet publication occurs.
