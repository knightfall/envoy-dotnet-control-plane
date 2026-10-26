# Schema-tooling and binding verification

The [T02 binding proof](../schemas/BINDINGS.md) documents the generated Protocol project, exact test mapping, provider limitations and clean NuGet consumer. Its [test project](Envoy.ControlPlane.Protocol.Tests/Envoy.ControlPlane.Protocol.Tests.csproj) references Protocol; `bindings verify` runs the same test source in a temporary package-only consumer as well. Both use the existing xUnit/Microsoft Testing Platform conventions. Neither binding test execution contacts an external service; Buf remote generation and package restore are preparation steps requiring network access.

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings verify --cache ../upstream-sources
```

All five maintained suites are registered in [Envoy.ControlPlane.slnx](../Envoy.ControlPlane.slnx) and the [schema verification workflow](../.github/workflows/upstream-verification.yml). The solution-level command is `dotnet test --solution Envoy.ControlPlane.slnx --configuration Release` after generation; focused project commands remain available. All project dependency versions come from [Directory.Packages.props](../Directory.Packages.props), with project dependency graphs recorded in lock files.

The binding suite is organized by behavior: `CatalogTests`, `AnnotationTests`, `SerializationTests`, `ServiceContractTests` and `GeneratorCompatibilityTests`. Shared inspection helpers and RPC fixture data live in `Envoy.ControlPlane.Protocol.Tests/Support/`. A shared xUnit collection retains sequential execution. The clean package consumer copies all handwritten test/support sources, so this organization does not reduce the package proof to a subset of checks.

T03 adds `ValidationInventoryTests` and its shared descriptor-inventory helper. Three cases cover the real catalog's nested/combined rules, explicit false/empty-duration presence, and skipped/Any fields. The expanded Protocol suite has 12 tests, including a regression for upstream XML documentation; the clean consumer includes those same sources and copies documentation through the SDK setting.

The [source validation proof](../schemas/VALIDATION.md) is in `Envoy.ControlPlane.Validation.Tests`. Its 18 native MTP cases cover the 33-message corpus, conditional oneofs, full descriptor identity, structured violations, fail-fast, descriptor faults/preloading, cold/warm concurrency, immutable source inputs and dependency isolation. `validation verify` invokes the independent Go oracle and runs the full suite again from a fresh NuGet consumer, including JSON fixtures and linked helpers. The earlier published-provider probes under `tests/validation/` remain historical research and are not used by the maintained CI entry points; its pinned `GoOracle` remains active.

## Upstream CLI suite

The [test project](Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj) references the main [CLI project](../tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj) and exercises its `upstream` commands through subprocesses. MSBuild builds and copies the referenced executable and dependencies. The fixture invokes that actual binary, rather than compiling a copy or maintaining a separate script-build target. The offline CI job runs its project command; the complete repository job also discovers it through the solution.

[BindingIntegrityTests.cs](Envoy.ControlPlane.Upstream.Tests/BindingIntegrityTests.cs) also exercises the binding tool's checksum helper through the referenced production assembly. Four platform-representation cases verify the same independently calculated fingerprint with Windows/Unix separators and CRLF/LF contents, including paths whose native sort order differs. A content-change case verifies that source changes still produce a distinct, exact fingerprint. These tests run in the existing offline CI matrix.

## Framework and execution

- .NET SDK **10.0.400**, target **net10.0**.
- xUnit.net v3 APIs through **xunit.v3.mtp-v2 4.0.2**; Microsoft Testing Platform **2.5.1**.
- Native MTP command mode selected in [global.json](../global.json).
- Roslynator.Analyzers **5.0.1** and warnings as errors in both the CLI and test project.
- Direct and transitive test dependencies pinned in [packages.lock.json](Envoy.ControlPlane.Upstream.Tests/packages.lock.json).

Run from the repository root:

```sh
dotnet restore tests/Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj --locked-mode
dotnet test --project tests/Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj --configuration Release --no-restore --results-directory artifacts/test-results --report-xunit-trx --report-xunit-trx-filename upstream.trx
```

The test command builds the test project and CLI with their analyzers before running the suite. `--no-build` can be used after that build when rerunning unchanged binaries. The `--project` form and report arguments are native MTP syntax; no VSTest bridge separator is used. The framework's MTP integration is described in [xUnit's documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).

## Offline fixture contract

[UpstreamFixture.cs](Envoy.ControlPlane.Upstream.Tests/UpstreamFixture.cs) creates a fresh temporary repository and sibling cache per test. The fixture has four synthetic source archives, a three-input import closure and a six-file inventory, including legacy/contrib exclusions. Its expected catalog is declared explicitly and is never generated by the CLI under test. Test fixture revisions, licenses and schemas are synthetic data, with no claim of upstream compatibility.

Every CLI invocation uses `--offline`. Restoring the test/framework packages requires network access on the first build; the test executions require no network or upstream downloads. Tests leave the checked-in schema lock, catalog and sibling Go checkout untouched. Subprocesses have a bounded timeout and are terminated on timeout; a timeout fails the test.

Every rejection case asserts exit code **1**, a specific diagnostic, empty success output, and an unchanged fingerprint of its fixture files. Artifact diagnostics also identify the affected archive. The valid case asserts exit code **0**, successful verification output, exact closure/inventory counts, and unchanged inputs.

## Requirement coverage

Test methods are in [UpstreamCliTests.cs](Envoy.ControlPlane.Upstream.Tests/UpstreamCliTests.cs).

| Requirement | Test |
|---|---|
| Valid inputs, transitive non-v3 import, legacy/contrib exclusions and read-only audit | `Sync_ValidOfflineFixture_VerifiesClosureAndPreservesInputs` |
| Missing required source | `Sync_MissingRequiredSource_IdentifiesSource` |
| Duplicate source owner entry | `Sync_DuplicateSource_IdentifiesDuplicate` |
| Altered pin versus baseline | `Sync_AlteredSourcePin_RejectsBaselineMismatch` |
| Floating source revision | `Sync_FloatingSourceRevision_RejectsNonCommitInput` |
| Altered archive checksum | `Sync_AlteredArchiveChecksum_RejectsCachedArchive` |
| Altered license checksum | `Sync_AlteredLicenseChecksum_RejectsNotice` |
| Missing license entry | `Sync_MissingLicenseEntry_RejectsIncompleteNoticeInventory` |
| Removed or changed catalog entry, even with matching lock fingerprint | `Sync_ModifiedCatalogWithMatchingFingerprint_RejectsSemanticDrift` — two theory cases |
| Altered catalog fingerprint | `Sync_AlteredCatalogFingerprint_RejectsLockDrift` |
| Unresolved import | `Sync_UnresolvedImport_IdentifiesImportPath` |
| Missing transitive schema source | `Sync_MissingTransitiveSource_IdentifiesUnownedImport` |
| Missing registry source evidence | `Sync_MissingRegistryEvidence_RejectsManifestGap` |
| Missing registry patch | `Sync_MissingRegistryPatch_RejectsPatchGap` |
| Altered module replacement | `Sync_AlteredModuleReplacement_RejectsModuleDrift` |
| Missing cached archive in offline mode | `Sync_MissingOfflineArchive_ReportsMissingArtifact` |
| Verified export selection and repeatability | `Export_ValidFixture_WritesOnlySelectedVerifiedInputs` |
| Export rejects catalog drift before creating output | `Export_DriftedCatalog_RejectsBeforeWritingInputs` |

The offline matrix runs 19 provenance/export cases and five binding-fingerprint cases. Platform execution results belong to the current PR checks and their uploaded TRX reports.

## CI and full-source evidence

[Upstream verification](../.github/workflows/upstream-verification.yml) runs the same project command for pull requests, pushes to `main`, and manual runs. Its Linux/macOS/Windows matrix restores in locked mode, builds with Roslynator, runs the offline suite and uploads TRX results. GitHub Actions are pinned to commit SHAs. Platform execution results belong to the PR checks; the local result above covers macOS arm64.

The separate [full-source provenance audit](../schemas/PROVENANCE.md) verifies all 523 real pinned schemas and compares rate-limit descriptors using the pinned protoc executable. It was rerun successfully after these tests were added. Fixture tests do not substitute for that evidence. The full protoc inspection remains macOS arm64 only until other compiler artifacts are explicitly pinned; no runtime-library or Envoy interoperability claim is made here.

## Core and hosting package consumers

The [executable probes](consumers/README.md) restore the freshly packed Core and ASP.NET packages into separate NuGet caches. They load the candidate assembly, use transitive Protocol contracts, verify exact dependency and shared-framework boundaries, and build an ASP.NET application host without opening a listener. `repository verify` stages their `.csproj.template` files into temporary projects; the templates do not join the source-build solution or require an unpublished package during bootstrap.

## Resource and snapshot suite

[T05 evidence](../docs/plan/T05-RESOURCES-AND-SNAPSHOTS.md) maps requirements to the Core tests in `Envoy.ControlPlane.Tests`. Its 38 native MTP cases cover naming, TTL indexing, partial snapshots, stable message replacement, opaque versions, deterministic serialization, unknown fields, explicit consistency and application-owned descriptor registration. The checked-in 19-case JSON corpus declares expected outcomes independently of the implementation.

`repository verify` runs all 92 solution cases, passes the corpus wire payloads to the [pinned Go snapshot oracle](resources/README.md), and runs all 38 Core cases again against the newly packed NuGet package in a fresh cache. These checks do not exercise network streams or a live Envoy proxy. T05 has no timing-based race tests; [T06](../docs/plan/T06-WATCHES.md) adds ten watch/contract cases with explicit gates and bounded failure timeouts. Core now has 48 cases and the solution has 102; the fresh package consumer runs all 48. Actual cache registration/publication and stream cleanup remain later integration evidence.

## SotW SnapshotCache verification

[T07 evidence](../docs/plan/T07-SNAPSHOT-CACHE.md) maps 21 new native cases to publication, selection, node hashing/status, clear/cancel, custom snapshot errors, lazy materialization and TTL wrappers. Core now has 69 native cases and the solution has 123. The new checked-in `cache-cases.json` declares 22 event traces; one corpus test asserts every expected observation and exports original wire messages for Go. The shared pinned oracle also runs those traces through the real upstream SnapshotCache. `repository verify` checks trace membership and compares response names, versions, returned metadata, registration counts and clear/cancel results. All 69 Core cases also run against the freshly packed package.

Concurrency tests use explicit gates and bounded failure waits. They observe a publication attempted during lookup and a blocked consumer while another node progresses; they add no sleep loop or timing-based success rule. T07 is a SotW cache proof. Delta delivery, heartbeat timers, stream state and actual network/backpressure integration remain later tasks.

## SotW subscription transitions

[T08 evidence](../docs/plan/T08-SOTW-SUBSCRIPTIONS.md) adds 12 native cases, bringing Core to 81 and the solution to 135. Its checked-in 14-trace corpus verifies every wildcard, exact-name, prefix and returned-version observation against declared expectations, then exports the traces to the actual pinned Go subscription/options implementation. Native tests additionally exercise the response-name predicate, read-only metadata ownership and real SnapshotCache unsubscribe/re-subscribe with no version change. The package-only Core consumer runs the complete expanded suite. These are processing-loop-owned state tests; no race or timing framework is added, and stream-loop suppression remains T09/T10 evidence.

## gRPC/Kestrel transport probe

[Transport validation](../docs/plan/GRPC-KESTREL-VALIDATION.md) compares real loopback HTTP/2 calls using the existing generated ADS/EDS contracts against grpc-go. The non-packable `Envoy.ControlPlane.TransportProbe` is a server executable, separate from the four MTP suites. Its CLI check names and assertions are listed in the report. Linux executes 46 functional checks and 90 measured trials; macOS executes 30 checks and 60 trials. These counts do not change the solution's unit-test count.

The maintained commands are `transport verify` and `transport benchmark` through the repository CLI. Go source is under `resources/GoOracle/transport/` and uses the existing module lock. Linux CI is a dedicated workflow; existing solution verification builds the host on all configured platforms. Actual xDS protocol behavior, service inventory and released-Envoy acceptance remain separate work.

`TransportSummaryTests.AllocationRegression_ExcludesEveryGroupForAffectedRuntimeAndRetainsOtherRuntime` verifies that a regressing allocation checkpoint excludes the entire affected runtime series while preserving valid Go results and raw null/checkpoint data. This adds one offline reporting case: 25 offline CLI tests and 136 solution cases in total.

## SotW stream engine

[T09 evidence](../docs/plan/T09-SOTW-STREAM-ENGINE.md) maps 37 native cases to request/callback order, nonce/watch lifetime, filtering/ownership, NACK cache reentry, serial writes and failure cleanup. `SotwRequestTests`, `SotwResponseTests` and `SotwLifecycleTests` use explicit channels/gates and bounded failure waits. `SotwStreamCorpusTests.SourceTraces_MatchDeclaredCallbacksWatchesAndSends` now verifies sixteen declared traces and exports their observations for the real pinned Go handler. [T10 evidence](../docs/plan/T10-ORDERED-ADS.md) adds 26 cases in `SotwOrderedAdsTests`, `SnapshotCacheChannelTests` and `SotwLifecycleTests`, covering independent readiness/order, producer FIFO, replacement draining, late unsubscribe filtering, custom types and asynchronous backpressure/disposal. Core has 144 cases and the solution has 199. The fresh package consumer includes all Core sources and fixtures.

The repository CLI runs the Go stream comparison as part of `repository verify` and saves evidence under `obj/upstream/results/streams/`. The tests use the transport-independent loop; discovery adapters and actual xDS HTTP/2/Envoy acceptance remain later tasks.

## ASP.NET Core hosting

[T11 evidence](../docs/plan/T11-ASP-NET-HOST.md) maps 23 tests in `Envoy.ControlPlane.AspNetCore.Tests` to nine real HTTP/2 generated clients, ACK/update/NACK, registered options, advertised/deferred endpoint inventory, status mapping, ordinary host authentication/authorization, call cancellation and host shutdown. The solution includes this project and the runnable example, bringing discovery to 222 cases. `repository verify` runs all 23 hosting tests again in a fresh package-only consumer and checks the updated hosting package graph. Tests use ephemeral Kestrel listeners; they do not substitute direct adapter calls for transport evidence.

## Preview resource helpers

[T20 preview evidence](../docs/plan/T20-PREVIEW-HELPERS.md) maps three Core cases and two HTTP/2 ADS cases to the sample graph, stable replacements, helper/Any outcomes and both stream ordering modes. Core has 147 cases, Hosting has 25 and the solution has 227. The eight declared helper cases and the exported sample graph run through the actual pinned Go helpers in `repository verify`; the existing snapshot decoder is shared rather than duplicated. Focused source links make the exact sample graph available in both fresh package test consumers. Released-Envoy proof remains separate.

## Preview differential harness

[T21 preview evidence](../docs/plan/T21-PREVIEW-DIFFERENTIAL.md) maps V05–V15 to existing native tests and pinned Go scenarios. The cache corpus now has 23 traces, including actual retained-response re-observation after replacement; the stream corpus has 22, including rejected open/request and missing ADS type in both modes. Go writes its actual stream observations. The CLI compares reports using a per-stream nonce bijection and rejects an injected stale association on every verification run. Caller versions, errors, event order and disposal counts remain exact. Seventeen `SotwTraceComparerTests` cases run in the offline matrix, bringing tooling to 42 and solution discovery to 244 cases. Core (147) and Hosting (25) remain fully exercised by clean package consumers.

## Preview lifecycle verification

[T22 preview evidence](../docs/plan/T22-PREVIEW-LIFECYCLE.md) maps existing deterministic race/failure tests and eight added cases to V14/V15/V28–V30. Core (149) verifies cancellation while a writer is blocked in both modes; Hosting (31) adds active callback cancellation observed before any host stop deadline and seeded HTTP/2 repetitions (128 streams, 512 checked responses across both modes). Exact watch, callback, close and task-completion assertions accompany diagnostic process-memory samples. The repository verifier retains both JSON reports under `obj/upstream/results/lifecycle/`, included in existing CI artifacts. The complete solution has 252 cases and all Core/Hosting tests run again as fresh package consumers. These bounded runs do not assert a memory budget, throughput SLA or full proxy acceptance.

## Released Envoy integration

[T23 evidence](../docs/plan/T23-PREVIEW-ENVOY.md) documents the separate `Envoy.ControlPlane.EnvoyProbe` and `envoy verify` command. Ordinary unit/package verification builds this executable without requiring Docker. The external command runs an exact official Envoy binary with the actual hosting/Core code in a pinned Linux SDK container, checks ten named V31 outcomes per mode and records traffic, typed admin state, NACK associations and same-process reconnect. The local Linux arm64 result passes 20/20; the dedicated Linux amd64 workflow supplies separate exact-head evidence. Six `EnvoyResultsTests` cases run in the offline matrix and reject incomplete/failed reports, bringing tooling to 48 and the solution to 258. No extra runtime/library dependency or schema changes are introduced.
