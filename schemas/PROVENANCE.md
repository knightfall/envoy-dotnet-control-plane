# T01: upstream input provenance

Recorded 24 October 2026 for [T01 / issue #2](https://github.com/knightfall/envoy-dotnet-control-plane/issues/2). Source work is implemented on `feat/t01-upstream-input-lock`, with remaining G0 acceptance noted below. This report records source and descriptor inspections. It does not establish C# binding-provider compatibility, validator equivalence, or Envoy interoperability.

## Artifacts

- [upstream.lock.json](upstream.lock.json): immutable source commits, archive checksums, registry evidence and build-only patches, licenses/notices, all seven Go module manifests and replacements, inspection tools, and the catalog fingerprint.
- [catalog.json](catalog.json): exact source path, import path, package, imports, SHA-256 and disposition for each inventoried schema.
- [Upstream.cs](../scripts/Upstream.cs): a C# file-based app for source retrieval, catalog reconstruction and descriptor inspection.
- [License and notice inventory](NOTICES.md).
- [Top-level baseline](../upstream-baseline.json).

## Immutable sources

| Input | Commit | Origin of selection |
|---|---|---|
| Go behavior and generated-descriptor reference | `67c5345367582741334e34ffc66a561b0ce32946` | Reviewed Go baseline |
| Envoy API | `82a4424f760b6ba3ffc9f2911389d061656f35c8` | Go `envoy/COMMIT` |
| Rate-limit configuration API | `0d9cfd0b3934c6c66bd97544592f3f539e7138cc` | Original `envoyproxy/ratelimit` schemas; descriptor comparison below |
| CNCF xDS / UDPA | `ee656c7534f5d7dc23d44dd611689568f72017a6` | Envoy Bazel lock, xDS source manifest |
| PGV annotations | `92b9a7df69ca9f71bfc492f7a90adf4d36eab569` | Envoy registry manifest for PGV v1.3.3 |
| Protobuf definitions | `35cd01f9fe9afbeea38cc7b979a3b6bfcde82c03` | Envoy registry manifest for protobuf v35.1 |
| Google APIs | `2193a2bfcecb92b92aad7a4d81baa428cafd7dfd` | Envoy Bazel lock, Google APIs source manifest |
| OpenTelemetry proto | `c0a98a1847d3124ac5f9ecd02d0e2d2732bbb590` | Envoy Bazel lock, v1.8.0 source manifest |
| Prometheus client model | `eb136e513d419e0c31ad750922f0a6f7675c2dee` | Envoy registry manifest for v0.6.2 |
| CEL definitions | `7f3c4c513b42d471d0be9439bf5bde38f45f8404` | Transitive Envoy Bazel lock, CEL spec v0.25.1 |

Every source archive URL uses its full commit, with a SHA-256 in the lock. Registry metadata uses the content hashes recorded in Envoy's `MODULE.bazel.lock`. The referenced registry patches were inspected: they change build files or C++ sources, with no `.proto` edits. Their checksums and touched paths are locked; they are not applied to the schema inputs. Envoy's separate `api/buf.lock` remains available as evidence in the pinned archive; this catalog follows its Bazel schema origins rather than mixing the two dependency selections.

Go module requirements are recorded separately from schema origins. In particular, the Go xDS dependency resolves to `dba9d589def2`, while Envoy's schema build selects `ee656c7534f5d7dc23d44dd611689568f72017a6`. These are distinct inputs. T02 must compare the selected provider's descriptors, imported options and type ownership against this catalog; a Go dependency version is not evidence of a schema source revision.

The root Go module replaces Envoy and ratelimit with `./envoy` and `./ratelimit`. The child modules and the version-qualified Envoy replacement are preserved verbatim in `goModules`. A version-qualified replacement is not treated as an unconditional override. The source archive, module-file fingerprints and `go.sum` fingerprints identify the actual reviewed module set, including deferred and tooling modules.

## Catalog selection

The seed is the standard Go-mirrored `/v3/` and `/v3alpha/` catalog, Envoy's annotation definitions, and both rate-limit configuration schemas. The closure follows every import, irrespective of version-directory spelling. This includes common `Any` payloads such as HTTP connection manager, router and TLS configuration, rather than only the discovery envelopes.

| Disposition | Files | Meaning |
|---|---:|---|
| Generate | 490 | 486 standard schemas, two Envoy annotation definitions, two rate-limit schemas |
| Dependency | 33 | Required external schemas, including PGV, UDPA/xDS options and protobuf definitions |
| Deferred legacy | 182 | Go-mirrored legacy catalog; T28 scope decision required |
| Deferred contrib | 47 | Contrib catalog; T28 scope decision required |
| Excluded | 3 | Envoy API Bazel descriptor-library test fixtures |
| Total inventory | 755 | All 720 Envoy API schemas plus 35 selected external/rate-limit inputs |

There are **523 selected inputs**: 488 Envoy, 16 CNCF xDS/UDPA, seven protobuf, five Google APIs, two CEL, two ratelimit, and one each from PGV, OpenTelemetry and Prometheus. `catalog.json` records every individual file and checksum, including deferred/excluded Envoy API files. Unused files in external archives are not release inputs. Runtime library source files and the matcher test proto are not schema-generation inputs.

Original validation options and their definitions are retained. No PGV-to-Protovalidate overlay has been created; annotation migration and rule inventory belong to T03. Imported matcher descriptors do not authorize the standalone matcher engine deferred under T27. Catalog service definitions do not expand the runtime service matrix. Protobuf well-known types must have one runtime owner in T02, rather than being regenerated into a competing assembly.

No application-specific custom extension is selected for release generation. T05 supplies descriptor/`Any` handling for application messages. Adding a custom schema to the release catalog requires an immutable source and checksum through the same process.

## Rate-limit source comparison

Original schemas are at `api/ratelimit/config/ratelimit/v3/rls_conf.proto` and `api/ratelimit/service/ratelimit/v3/rls_conf_ds.proto` in the pinned rate-limit repository. Both use its `api/` import root.

The configuration source at the selected revision includes `RateLimitDescriptor.metadata = 8`, matching the reviewed Go output. Both schemas were compiled with protoc 35.1 and compared with the complete `FileDescriptorProto` bytes embedded in the Go generated files. The comparison retains messages, enums, field numbers/types, options, imports and RPC streaming flags; it does not ignore an arbitrary set of differences.

| Schema | Result |
|---|---|
| Configuration messages | Definitions match after the one filename normalization below |
| Configuration discovery service | Complete descriptor matches without normalization; `StreamRlsConfigs` is bidirectional streaming and `FetchRlsConfigs` is unary; there is no Delta RPC |

The Go configuration header names `ratelimit/config/ratelimit/v3/rls_conf.proto`, but its embedded descriptor names **`api/ratelimit/config/ratelimit/v3/rls_conf.proto`**. The selected source compiled under its `api/` import root names it without that prefix. This is the sole allowed normalization in the comparison, explicitly locked alongside both original descriptor hashes. The service descriptor already uses the name without `api/`.

This resolves source provenance by matching definitions. The historical generation invocation is not recorded upstream. **T02 must make the descriptor filename/ownership decision explicit before G1**; this report does not silently rename generated provider metadata.

## Retrieval and inspection

Use the pinned [.NET SDK](../global.json), currently **10.0.400**. The app references **Google.Protobuf 3.35.1** with an exact `#:package` directive. Inspection verifies the loaded assembly hash against the locked NuGet package. These are provenance-tool dependencies, not the library's T02 provider decision. File-based apps and their package directives are documented in [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps).

**Roslynator.Analyzers 5.0.1** is also pinned in the file-based app and source lock. The root [.editorconfig](../.editorconfig) promotes its enabled rules to warnings, and the app treats warnings as errors. Build analysis identified 21 diagnostics, which were addressed without suppressions: namespace placement, generated reusable regexes, braces and simpler conditionals. The final analyzer build completed without diagnostics. Roslynator is an analyzer-only development dependency; the package declares Apache-2.0 and no package dependencies. Its [configuration](https://josefpihrt.github.io/docs/roslynator/configuration/) is separate from binding-provider and validator decisions.

Run from this repository's root. Choose an external cache, for example a sibling `upstream-sources/` folder. It contains checksummed archives and metadata, without nested Git repositories. Retrieval does not edit or run scripts from the Go checkout. Selected source files for compilation are materialized in a temporary inspection directory under that cache and removed afterward.

```sh
dotnet build scripts/Upstream.cs
dotnet run --file scripts/Upstream.cs -- sync --cache ../upstream-sources
dotnet run --file scripts/Upstream.cs --no-restore -- sync --cache ../upstream-sources --offline
dotnet run --file scripts/Upstream.cs -- inspect --cache ../upstream-sources
```

`--offline` controls source/tool downloads. For an entirely offline run, first restore/build the file-based app and populate the artifact cache, then use `--no-build --no-restore` with `--offline`. Source synchronization uses framework archive/HTTP/JSON APIs and is portable; the currently locked **protoc inspection executable is macOS arm64 only**. Add an explicitly checksummed compiler artifact before claiming inspection support on another platform. T04 will establish the runtime package/CI toolchains separately.

Intentional input updates change the source lock first, then reconstruct the catalog:

```sh
dotnet run --file scripts/Upstream.cs -- sync --cache ../upstream-sources --write-catalog
```

Review the generated diff, update `catalogSha256` in the lock, and rerun `sync` and `inspect`. `--write-catalog` is an update operation, not a passing audit. No command updates a branch, tag or source pin automatically.

## Observed evidence and remaining acceptance

The C# app was built with SDK 10.0.400 on macOS arm64, with Google.Protobuf 3.35.1. Commands used a temporary CLI home, NuGet package cache and source cache to keep generated artifacts outside this repository:

```sh
DOTNET_CLI_HOME=/private/tmp/envoy-dotnet-cli NUGET_PACKAGES=/private/tmp/envoy-dotnet-nuget dotnet build scripts/Upstream.cs --no-restore --nologo
DOTNET_CLI_HOME=/private/tmp/envoy-dotnet-cli NUGET_PACKAGES=/private/tmp/envoy-dotnet-nuget dotnet build scripts/Upstream.cs --no-restore --nologo -t:Rebuild -p:ErrorLog=/private/tmp/envoy-dotnet-upstream/roslynator.sarif
DOTNET_CLI_HOME=/private/tmp/envoy-dotnet-cli NUGET_PACKAGES=/private/tmp/envoy-dotnet-nuget dotnet run --file scripts/Upstream.cs --no-restore -- sync --cache /private/tmp/envoy-dotnet-upstream/cache --offline
DOTNET_CLI_HOME=/private/tmp/envoy-dotnet-cli NUGET_PACKAGES=/private/tmp/envoy-dotnet-nuget dotnet run --file scripts/Upstream.cs --no-build --no-restore -- inspect --cache /private/tmp/envoy-dotnet-upstream/cache --offline
DOTNET_CLI_HOME=/private/tmp/envoy-dotnet-cli NUGET_PACKAGES=/private/tmp/envoy-dotnet-nuget dotnet run --file scripts/Upstream.cs --no-restore -- sync --cache /private/tmp/envoy-dotnet-upstream/clean-cache
```

Observed: all ten archive checksums, twelve root license/notice fingerprints, registry evidence/patch checksums, seven module manifest/replacement inventories, baseline pins, catalog checksum and the complete import closure agree. Protoc 35.1 compiled all 523 selected schemas without warnings or errors. Both rate-limit descriptor comparisons passed with only the declared configuration-filename normalization.

The final `sync` command began with a nonexistent external cache and retrieved all locked source archives and registry evidence successfully. The rebuilt catalog matched the recorded inventory and fingerprint. Subsequent offline source inspection used the same locked data. The sibling Go checkout remained clean at its recorded commit.

V01's changed/missing-lock negative scenarios have not been executed. G0 remains pending those cases and review; this issue should remain open until its completion evidence is published. No C# runtime bindings, library build, unit tests, validator conformance or live Envoy exercise is claimed. There are no unresolved source-origin blockers in the selected catalog; T02/T03 binding/metadata decisions remain separate gates.
