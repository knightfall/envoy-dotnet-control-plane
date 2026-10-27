# Repository commands

Run the project-based CLI from the repository root. The pinned SDK, central package versions, checked-in dependency locks, Roslynator and warnings-as-errors policy apply to both tooling projects.

| Project | Responsibility | Dependencies |
|---|---|---|
| [Envoy.ControlPlane.Tools](Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj) | Source verification/export, Buf generation, exact C# corrections, tests, Go comparisons and package-consumption proof | Google.Protobuf; no generated Protocol or evaluator reference |
| [Envoy.ControlPlane.SchemaTools](Envoy.ControlPlane.SchemaTools/Envoy.ControlPlane.SchemaTools.csproj) | Typed descriptor overlay and PGV rule inventory | Generated Protocol and its annotation contracts |

The main CLI builds before any generated contracts exist. Generation exports verified source inputs, generates annotation contracts and bootstrap Protocol through Buf, builds the schema helper, then applies the typed overlay and generates final Protocol. This order avoids a circular build dependency. The main CLI calls its source-export and correction code directly; only the generation-dependent schema helper runs as another .NET application.

`Upstream/`, `Bindings/`, `Validation/`, `Repository/`, `Transport/` and `Envoy/` contain focused implementation files. `Program.cs` only routes commands. Common tooling build settings live in [Directory.Build.props](Directory.Build.props); package versions remain in [Directory.Packages.props](../Directory.Packages.props).

The root [.gitattributes](../.gitattributes) keeps tracked text at LF on all platforms, including Windows checkouts with `core.autocrlf=true`. Configuration fingerprints verify exact bytes. Generated-source fingerprints normalize LF contents and forward-slash paths before ordinal sorting.

## Bootstrap

Use the .NET SDK in [global.json](../global.json) and Go version in [sources.lock.json](../schemas/validation/sources.lock.json). Choose a writable source-cache directory outside the checkout. The commands retrieve and verify pinned remote artifacts; initial package restore and Buf remote-plugin generation require network access.

```sh
dotnet restore tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj --locked-mode
dotnet build tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj --configuration Release --no-restore
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release --no-build -- upstream --help
```

## Source verification

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream sync --cache <external-source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream inspect --cache <external-source-cache> --offline
```

`sync` verifies source archives, licenses, baseline pins, module replacements and catalog/import closure. `export` additionally writes selected inputs beneath Protocol's `obj/upstream/`. `inspect` performs the separately pinned provenance compiler check; its executable is currently limited to macOS arm64. See [provenance evidence](../schemas/PROVENANCE.md).

## Binding generation and package proof

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <external-source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings verify --cache <external-source-cache>
```

`generate` invokes the pinned Buf executable and official plugins. Original schemas, generated-code corrections, descriptors and source inventories remain fingerprinted. The helper's locked restore happens after bootstrap contracts exist.

`verify` completes generation, packs Annotations and Protocol, runs the Protocol suite, and stages all handwritten test sources and linked helpers in a fresh package-only consumer. Its NuGet cache is under the temporary consumer's `obj/`; package content cannot enter source globs. See [binding evidence](../schemas/BINDINGS.md).

## Source-built validation

After binding generation:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- validation verify
```

This checks materialized evaluator fingerprints and pinned conformance inputs, runs the validation suite and Go oracle, packs the evaluator, then repeats the full suite against fresh Annotations/Protocol/Validation packages. The suite includes 2,748 stable upstream conformance cases with seven explicit date-relative exclusions; the Envoy/Go PGV corpus is separate. Source hashes, generated fixture fingerprints, reports and licenses are retained. See [T26 progress](../docs/plan/T26-VALIDATION-CONFORMANCE.md) for current scope. Validation stays explicit and optional. See [T03 evidence](../schemas/VALIDATION.md) for adaptations and remaining GV work.

The overlay writes `catalog.generation.pb` with upstream source comments for C#/gRPC generation and normalized `catalog.pb` for packaged contract comparison. Protocol and Annotations include generated XML documentation.

## Complete repository verification

T04 adds [Envoy.ControlPlane.slnx](../Envoy.ControlPlane.slnx), containing all maintained projects. Generate contracts first, then run:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <external-source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
```

`repository verify` checks solution registration, verifies pinned validation inputs, performs locked solution restore/build/test, compares validation, snapshot, SotW cache and subscription corpora with the pinned Go implementations, and packs all five runtime packages with symbols. It inspects readmes, licenses, assemblies, XML documentation where supplied, and separate portable-symbol packages. The source-built evaluator retains upstream's existing XML-documentation limitation.

The command runs the full Protocol, Validation, Core and Hosting suites against fresh NuGet packages, then runs the Core and ASP.NET consumers under [tests/consumers/](../tests/consumers/README.md). Each consumer restores into a separate empty NuGet cache. Core's exact package graph excludes hosting and evaluator dependencies; ASP.NET adds its shared framework and Core dependency without an evaluator. Core consumers exercise resource snapshots, explicit consistency and Delta version construction. The full Core suite also verifies one-shot watches and custom contracts. The complete selected root surface, including SotW/Delta, Fetch, Linear/Mux and caller-owned SotW heartbeat scheduling, is implemented. Root candidate packages use the `Knightfall.Envoy.ControlPlane` prefix while retaining assembly names; the optional evaluator remains experimental. See the [root compatibility report](../docs/compatibility/root-1.0.md) for scope and proxy limits.

Solution tests use native MTP through `dotnet test --solution Envoy.ControlPlane.slnx`. All five maintained suites are registered, including Core and Hosting. Reports and consumer locks are under Protocol's `obj/upstream/results/`; packages and `.snupkg` files remain under its `bin/packages/`. This is local candidate packaging, not a NuGet publishing command.

To collect portable coverage while running the same verification, use `repository verify --coverage`. It runs the solution's declared test projects with **Coverlet.MTP 10.1.0**, keeping each project's results in its own directory to avoid report collisions. The pinned local **ReportGenerator 5.5.11** tool merges those reports into HTML, Cobertura XML and a Markdown summary under `obj/upstream/results/coverage/report/`. The command restores that tool from `.config/dotnet-tools.json`; it needs no global tool installation. Fresh package consumers and Go comparisons still run afterward.

Coverage includes test-loaded `Envoy.ControlPlane*` assemblies, including the source-built optional evaluator, and excludes test assemblies and generated bindings under `obj/generated`. It reports executed code, not all repository files, full behavioral parity or an acceptance threshold. Ordinary verification remains available without instrumentation. CI collects coverage only on Linux; all platforms retain their existing functional and package checks. The HTML/Cobertura artifact needs no hosted coverage service or GitHub Code Quality subscription.

The shared `.config/coverlet.mtp.appsettings.json` specifies exclusions explicitly. This prevents Coverlet.MTP's test-assembly naming defaults from excluding similarly named product assemblies. Verification rejects a merged report that lacks measured source lines for Core, hosting or the optional validator. Earlier reports that only contained the tooling assembly are not comparable with the corrected library coverage.

## Focused maintenance commands

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- patch <generated-directory> [binding-lock]
dotnet restore tools/Envoy.ControlPlane.SchemaTools/Envoy.ControlPlane.SchemaTools.csproj --locked-mode
dotnet run --project tools/Envoy.ControlPlane.SchemaTools -- inventory <output-json>
dotnet run --project tools/Envoy.ControlPlane.SchemaTools -- overlay <original-descriptors> <migrated-descriptors> <output-descriptors>
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream report <output-json>
dotnet run --project tools/Envoy.ControlPlane.Tools -- upstream diff <before-json> <after-json> <output-json>
```

Run schema-helper commands after bootstrap generation. `patch` accepts only exact locked input or already corrected fingerprints and verifies replacement counts. The inventory and overlay use typed protobuf APIs. Their source is linked into the relevant package-consumer suites so those suites do not acquire a dependency on a tooling package.

The upstream suite references the main project and exercises its actual CLI in subprocesses. MSBuild tracks that dependency without a separate script-build target. [Tests](../tests/README.md) and [CI](../.github/workflows/upstream-verification.yml) define maintained entry points. Generated inputs and source remain under `obj/`; package output remains under `bin/`.

The earlier provider experiments and orchestration under [scripts/](../scripts/README.md) are retained historical research, outside both active projects and CI. Their removal remains deferred.

`upstream report` runs after generation and records source/tool pins, schema fingerprints, original PGV rules, derived options and unproven validation coverage. It does not fetch newer sources or approve changed locks. `upstream diff` needs no generated Protocol and retains exact added/changed/removed values. Save baselines outside `obj/upstream`, which generation replaces. [T24 readiness](../docs/plan/T24-PREVIEW-READINESS.md) documents update and verification gates. `repository verify` retains its snapshot and every consumer lock under `obj/upstream/results/` for CI upload.

## Migration verification

Observed on 25 October 2026 on macOS arm64, starting from merged T03 commit `4473df9748368f531240a0491e8eb8ecb95ad390`. Verification used an isolated copy with no `bin/`, `obj/` or generated contracts, the pinned .NET SDK **10.0.400**, Go **1.26.4**, and Buf **1.75.0**. The external source cache contained checksummed artifacts; each package-consumer proof used a fresh NuGet cache.

The main CLI built successfully before Protocol existed. Both tooling projects built with **0 warnings and 0 errors**. Full `bindings verify` and `validation verify` runs exited 0 through the new CLI:

```sh
dotnet restore tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj --locked-mode
dotnet build tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj --configuration Release --no-restore
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release --no-build -- bindings verify --cache <external-source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release --no-build -- validation verify
dotnet restore tests/Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj --locked-mode
dotnet test --project tests/Envoy.ControlPlane.Upstream.Tests/Envoy.ControlPlane.Upstream.Tests.csproj --configuration Release --no-restore
```

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Offline CLI and fingerprint suite | 24 | 0 | 0 |
| Protocol project | 12 | 0 | 0 |
| Fresh Protocol package consumer | 12 | 0 | 0 |
| Validation project | 18 | 0 | 0 |
| Fresh Validation package consumer | 18 | 0 | 0 |

The Go oracle matched **33/33** declared cases. Generation preserved **523** original descriptors, the **16** conditional-oneof corrections, and the existing **540 Protocol / 1 annotation** source fingerprints. Only the configuration-lock paths for moved helpers changed; their content hashes did not. The separate `upstream inspect --offline` audit compiled all 523 schemas and matched both rate-limit descriptors. The schema helper's `inventory` command reported the same 1,244 annotated fields across 523 schemas.

The existing assertions and suite sizes were preserved. Linux/macOS/Windows CI is configured to use the project commands; remote CI for this migration has not run before publication. These results preserve the bounded T03 proof and do not establish full T26/GV conformance or live Envoy interoperability.

## Transport validation

After binding generation, run `dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- transport verify` for functional checks or `transport benchmark` for those checks plus matched performance trials. Linux tests plaintext, TLS 1.2 and TLS 1.3; macOS tests plaintext and TLS 1.2. The commands use the non-packable transport host and a common Go driver from the pinned resource oracle module. They do not run the xDS engine.

The [transport report](../docs/plan/GRPC-KESTREL-VALIDATION.md) describes workload controls and limitations. Reports, raw checks/trials and server logs are written under Protocol's `obj/upstream/results/transport/`. Test keys stay in a temporary directory and are removed. The [Linux baseline workflow](../.github/workflows/transport-performance.yml) is **manual-only after acceptance**, using `workflow_dispatch` on the selected branch/ref. It publishes evidence and gates functional failures without imposing performance thresholds. Pull requests retain ordinary build/package and HTTP/2 functional tests without running the performance baseline.

## Released Envoy interoperability

After generation, run `dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- envoy verify` on a Linux/macOS Docker client connected to a supported Linux amd64/arm64 engine. [The image lock](../tests/resources/envoy-images.lock.json) pins Envoy 1.40.1 and SDK 10.0.400 by official per-platform manifest digest. The command builds the dedicated Envoy probe, extracts the exact proxy binary, and runs control plane/backends/proxy in one container with no external network or published host ports. Caller-owned output is retained under Protocol `obj/upstream/results/envoy/`; temporary binaries and owned containers are removed.

The command enforces all twenty V31 checks, including actual traffic, endpoint/route updates, NACK version/nonce/config retention, explicit repair, outage and same-proxy reconnect. [T23 evidence](../docs/plan/T23-PREVIEW-ENVOY.md) qualifies local/CI execution and scope. `repository verify` builds the probe and runs report-verifier tests without requiring Docker; `.github/workflows/envoy-interop.yml` runs the external proof and uploads its exact-head results. No NuGet or Docker image publication occurs.

The binding tool lock includes the official Buf 1.75.0 Linux arm64 executable for the native arm64 released-Envoy CI runner. Existing macOS/Windows/Linux x64 entries and output fingerprints are unchanged. A missing platform entry fails before generation; a downloaded or cached executable must match its locked checksum and version.

Root update reports include authored runtime/test/example/project, Go corpus, JSON fixture and workflow input fingerprints, alongside structured source/schema/tool/proxy locks and preserved validation rules. Upstream diff selects full current Go/.NET root corpora, native/package consumers and released-proxy verification when recorded inputs change; it does not execute them, select the manual performance baseline or approve GV. See [T24 root readiness](../docs/plan/T24-ROOT-READINESS.md).
