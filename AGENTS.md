# Repository guidance

These instructions apply to this Git repository. Resolve paths and run commands from the checkout root, regardless of its directory name or host environment.

## Start here

Read [README.md](README.md) for the implementation status and [docs/plan/IMPLEMENTATION-PLAN.md](docs/plan/IMPLEMENTATION-PLAN.md) for task scope and acceptance criteria. Plans describe intended behavior; they are not evidence that a feature is implemented.

| Path | Purpose |
|---|---|
| `Envoy.ControlPlane.slnx` | All maintained library, tooling and test projects |
| `src/` | Runtime libraries and package definitions |
| `tests/` | Automated verification, fixtures and shared test support |
| `tools/` | Project-based repository CLI and generation-dependent schema helper |
| `scripts/` | Retained historical research helpers |
| `schemas/` | Source/tool locks, catalog, annotation inventory and provenance |
| `docs/` | Architecture, decisions, parity requirements and implementation plan |

## Bootstrap and verification

- Use the .NET SDK specified in `global.json`. Do not silently upgrade the SDK, target frameworks or dependencies to make a build pass.
- Use the Go toolchain version recorded in the validation source lock for generation and comparison. Consumers of built packages do not need Go or Buf.
- A fresh checkout does not contain generated bindings. Read [tools/README.md](tools/README.md) and generate them before building projects that reference Protocol.
- Source and tool retrieval uses the canonical **remote HTTPS URLs and immutable commits/checksums** recorded in [upstream-baseline.json](upstream-baseline.json), [schemas/upstream.lock.json](schemas/upstream.lock.json), [schemas/bindings.lock.json](schemas/bindings.lock.json), and [schemas/validation/sources.lock.json](schemas/validation/sources.lock.json). These manifests are the source of truth for upstream locations; do not require another checkout or assume a sibling directory exists.
- Choose a writable source-cache directory outside the checkout. Do not depend on a previously populated cache or a globally installed Buf executable. The tooling retrieves and verifies its pinned artifacts. Initial dependency restore and remote-plugin generation require network access.

Run the provenance test project with locked dependencies:

```sh
dotnet restore tests/Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj --locked-mode
dotnet test --project tests/Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj --configuration Release --no-restore
```

Generate bindings, then build/test/pack the complete solution and verify package consumption with the following commands. Replace `<source-cache>` with the chosen cache directory:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
```

Use [tests/README.md](tests/README.md) for additional test commands and [.github/workflows/upstream-verification.yml](.github/workflows/upstream-verification.yml) for CI entry points. Check declared tool/platform support before claiming a verification result. Generated inputs and code belong under `obj/`; package output belongs under `bin/`.

## Scope and compatibility

- Match the selected upstream behavioral and protocol contract. Feature availability stops at the agreed parity scope; avoid speculative frameworks, policy layers and stronger guarantees.
- Generate contracts through **Buf CLI** with pinned official plugins. Preserve original upstream schemas and keep managed mode disabled unless a reviewed metadata change requires it.
- Preserve protobuf full names, field numbers/types, service contracts and original validation options. Do not invent or rename contract schemas to bypass generation problems. Investigate upstream support before proposing a workaround; document and verify any necessary generated C# correction.
- Keep one authoritative owner for each message and annotation definition. Google.Protobuf owns well-known types. Do not add overlapping binding assemblies.
- Keep behavioral baselines, schema revisions, generator versions and released-proxy versions distinct. An intentional update must record changed pins and compatibility evidence.
- Keep optional validation dependencies separate from Protocol, Core and hosting. Preserved annotation metadata and an installed evaluator do not, by themselves, establish validation equivalence.
- Qualify compatibility claims by the tested scope. Descriptor comparisons, serialization checks, validation conformance and live interoperability are separate forms of evidence.

## Readable code and organization

- Use descriptive names, ordinary control flow and one clear responsibility per method or type. Expand dense expressions; avoid single-letter helper names and multiple operations on one line.
- Keep command entry points short and show workflow phases in execution order. Put implementation in focused files and folders rather than hiding unrelated responsibilities in a large class.
- Use the project-based CLI under `tools/` for maintained repository commands. Keep the main CLI independent of generated Protocol so it builds from a fresh checkout; build the schema helper after bootstrap generation. Use ordinary project references for build dependencies and focused source links only where clean package-consumer tests require them.
- Group tests by behavior. Keep shared fixtures, contract data and descriptor-inspection helpers separate from test orchestration.
- Prefer typed protobuf APIs. Use reflection only where generated identity or contract inspection requires it, and keep that inspection focused.
- Explain constraints and decisions in comments. Avoid obvious narration, unnecessary wrappers and generic abstractions for small workflows.
- Follow `.editorconfig`, nullable and deterministic-build conventions. Keep **Roslynator** enabled and warnings as errors; investigate findings rather than suppressing them for convenience.

## Dependencies and tests

- Use **central package management** in `Directory.Packages.props`. Do not put versions on project `PackageReference` items. Keep project dependency lock files current and verify with locked restore. Materialized third-party evaluator source is fingerprinted and retains upstream style; Roslynator checks authored code and tests, and compiler warnings remain errors everywhere.
- Follow declared project targets, using `net10.0` by default. Broaden support only for a concrete consumer requirement backed by a consumption proof.
- Assert meaningful acceptance, rejection and boundary outcomes. Keep expected fixtures independent of the implementation and preserve existing assertions during refactoring.
- Prefer existing upstream schemas for descriptor/Any tests. Synthetic schemas are permitted in isolated source-tooling fixtures, not as replacement library contracts.
- Verify clean NuGet consumption when bindings or packaging change. The consumer must use the newly built package and include all relevant test/support sources.
- Register every maintained project in `Envoy.ControlPlane.slnx`. Keep executable package-consumer templates reachable through `repository verify`; historical provider probes remain excluded. Keep every implementation and test source reachable through the documented build and CI entry points. Run focused checks and the required package proof; do not treat a zero-test run or an unrun check as success.

## Collaboration and contribution

- Explain the purpose and scope of significant scripts or design choices. Discuss material ambiguity before dependent implementation, and honor requests to discuss before proceeding.
- Use repository issues and milestones for agreed tasks and acceptance evidence. Do not add hours, effort estimates or estimate calculations. Execution timeouts are not effort estimates.
- Inspect Git status and configured remotes before changing branches or publishing. Preserve existing work and keep changes scoped to the task.
- Use normal Git branches, commits and pull requests. Do not manufacture repository changes through blob/tree APIs.
- Provide concrete changes and a full review summary before pushing unless publication is already authorized for that scope. Merge only with authorization; do not request the same authorization repeatedly.
- Report commands run, pass/fail/skip counts, analyzer findings and tested platforms. Distinguish observed results from configured CI, pending checks and future acceptance gates.
