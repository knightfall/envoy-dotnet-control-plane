# T02 binding-provider proof

T02 baseline evidence below is retained from PR #34. The current T03 source-built validator adds derived metadata and a separate annotation owner; see [current validation evidence](VALIDATION.md) and its binding lock for final runtime descriptors and generated-source fingerprints.

Candidate: **owned generation through the Buf CLI**, using the original T01 schema inputs. Protocol targets **net10.0**. T03 must prove validation feasibility before G1 freezes this provider. No upstream `.proto` files are changed and no custom `.proto` files are added to the library or its binding tests.

## Provider assessment

The public NuGet feed and package contents were inspected on 24 October 2026. Assembly type definitions were read without executing candidate code. These are bounded rejection reasons, not a survey of every possible package.

| Candidate | Observed limitation |
|---|---|
| `Envoy.ControlPlane` 0.3.0 | `edcp.dll` lacks `AggregatedDiscoveryService`, `ExtensionConfigDiscoveryService`, `RateLimitConfig` and `RateLimitConfigDiscoveryService` definitions. Its package metadata does not establish the selected Envoy/schema commit. |
| `EnvoyProxy` 1.14.2 | Lacks `ExtensionConfigDiscoveryService` and both ratelimit configuration types above; its package targets netcoreapp3.1 and does not establish the selected source catalog. |
| `Envoy.Grpc` 1.0.1 | Contains ADS/ECDS but lacks both ratelimit configuration types. Its metadata does not establish the selected schema commit/import graph. |
| BSR-generated NuGet SDK | The public feed returned HTTP 401. Its [documented restore setup](https://buf.build/docs/bsr/generated-sdks/nuget/) requires authentication. Exact source/annotation/ratelimit coverage was therefore not established; it is unproved, not classified as intrinsically incompatible. |
| Buf CLI with official remote plugins | Generated the complete selected catalog from the verified source files. Remote generation succeeded without BSR NuGet credentials. Selected as the candidate below. |

Inspected NuGet archive SHA-256 values:

| Package | SHA-256 |
|---|---|
| `Envoy.ControlPlane` 0.3.0 | `27a973dcb8d6727efd7639c2c1466f3fc4a215f08075d17101c1a83b6d1c0f94` |
| `EnvoyProxy` 1.14.2 | `b085892cff9f5392b8def56cc04667920fbcfa2f795f575e5c54b8b41ae5e5fb` |
| `Envoy.Grpc` 1.0.1 | `d1cc27dd5fc4a2ad8f5e1a3c15fb18872d52387487fd6dd56c0faaeb786ed362` |

## Generation and ownership

[buf.yaml](../buf.yaml) defines the module containing only the exported T01 inputs. [buf.gen.yaml](../buf.gen.yaml) runs the official `protocolbuffers/csharp:v35.1` and `grpc/csharp:v1.84.0` plugins, each at revision 1. Managed mode is disabled, so no schema options are overridden. Well-known schemas remain compilation inputs but are excluded from generation because Google.Protobuf already owns their CLR types.

[bindings.lock.json](bindings.lock.json) pins Buf **1.75.0**, platform executable checksums, plugin versions/revisions, configuration hashes, the complete source descriptor-set hash, and the generated C# inventory hash. [Directory.Packages.props](../Directory.Packages.props) centrally manages every project dependency. Project lock files record their resolved versions and content hashes. Grpc.Tools is not used for generation or referenced by the projects.

The selected catalog contains **523 schemas**. Protocol owns **516** source descriptors and **540** generated C# files, including gRPC contracts. Google.Protobuf owns the other **7** well-known descriptors. No selected schema was dropped to make C# compilation pass. Existing legacy/contrib deferrals remain those declared by T01; this does not expand the library's runtime scope.

Protocol owns selected Envoy, xDS/UDPA, ratelimit, CEL, Google API/RPC, PGV, OpenTelemetry and Prometheus types and annotation definitions. Namespaces follow upstream `csharp_namespace` options or standard C# generator defaults. Examples include `Envoy.Config.Listener.V3`, `Ratelimit.Config.Ratelimit.V3`, `Validate` and `Google.Api`. Under a namespace rooted in `Envoy`, qualify `System.Type` because `Envoy.Type` is also a generated namespace.

Consumers must use this one owner for those definitions; adding Google.Api.CommonProtos or another overlapping Envoy catalog introduces duplicate identities. Protocol references only Google.Protobuf **3.35.1** and Grpc.Core.Api **2.84.0** at runtime. Callers choose their channel/hosting transports. No validator or CEL evaluator is added.

## C# generator limitations

Official Buf generation reproduces three compilation failures in the unchanged upstream schemas:

| Schema / message | Failure | Generated C# correction |
|---|---|---|
| Rate-limit expression `Descriptor` | Class name collides with its static `Descriptor` property (`CS0542`) | Rename that static property to `MessageDescriptor`; keep explicit `IMessage.Descriptor` working. |
| JWT-claim `Descriptor` | Same collision (`CS0542`) | Same correction. |
| ext-proc `BodyMutation` | `ClearBody()` method collides with `ClearBody` property (`CS0102`) | Rename only the clearing helper to `ClearBodyValue()`. |

[PatchBindings.cs](../tools/Envoy.ControlPlane.Tools/Bindings/PatchBindings.cs) accepts only the exact locked input or corrected file fingerprints and checks replacement counts. Unexpected output fails before compilation. These corrections change C# members, while preserving protobuf full names, field names/numbers, JSON names, serialized descriptors and binary encodings. No source schema or serialized descriptor is patched.

The ext-proc collision is documented in [protobuf issue #18063](https://github.com/protocolbuffers/protobuf/issues/18063); maintainers reported no accessor-renaming option. The [C# generator options](https://protobuf.dev/reference/csharp/csharp-generated/) do not supply a solution. The v36.1 generator source was also inspected: it still emits the static `Descriptor` property and per-field `Clear…` helpers. That source inspection is not a v36.1 execution result.

Excluding these messages would reduce the selected catalog; changing their schema names would change descriptor identity. The candidate therefore keeps the upstream schemas and records the C# corrections explicitly. T03 must assess any validation runtime's assumptions about those two static property names before G1.

## Descriptor and original-option proof

The package embeds the complete original Buf-built `FileDescriptorSet`, catalog, binding lock and [annotation inventory](annotations.json). The set is also fingerprinted in the binding lock. Tests compare all 523 runtime file descriptors with the source set, including original custom option definitions and values.

The comparison has two declared normalizations:

1. C# compact descriptors omit inferred `json_name` strings. Tests compare every effective field/extension JSON name first, then normalize its encoded presence.
2. Runtime descriptors omit extension-range `declaration` and `verification` values marked `RETENTION_SOURCE` in `descriptor.proto`. Their original values remain in the embedded source set. Tests do not remove PGV or other runtime annotation values.

The inventory records **22 extension definitions**, **1,244 PGV field-rule options**, and **127 oneof-required options**, with a fingerprint of original PGV values and top-level field-rule case counts. `None` means no scalar/collection case is selected; a message rule can still apply. T03 expands nested rule inventory and semantic feasibility; these counts do not claim that rules have been evaluated.

Ratelimit configuration uses the original API-root descriptor name `ratelimit/config/ratelimit/v3/rls_conf.proto`. The Go embedded descriptor's extra `api/` prefix is the known T01 difference. Field definitions, including metadata field 8, remain unchanged. The separate rate-limit configuration service has Stream/Fetch contracts and no Delta method.

## Repeatable commands

Run from the target repository root, with a source cache outside both repositories:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache ../upstream-sources
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings verify --cache ../upstream-sources
```

`generate` verifies and exports the upstream inputs, downloads/checks the pinned Buf executable if needed, runs `buf build` and `buf generate`, applies the three locked C# corrections, and checks all generated output hashes. Official remote plugins require network access. An existing source cache is reused after checksum verification.

`verify` additionally restores in locked mode, builds/packs Protocol, runs its binding tests, and copies those same tests into a temporary project that consumes only the newly built NuGet package. That consumer has a fresh package cache and no source/project reference. Its locked restore and all binding tests must pass. The temporary project is removed after its reports/lock are copied to `obj/upstream/`.

Generated files, descriptor output and test reports are under `src/Envoy.ControlPlane.Protocol/obj/`; local packages are under that project's `bin/packages/`. Original license/notice texts from the verified archives are packaged under `licenses/<source-id>/`. The main [CLI project](../tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj) routes `upstream` and `bindings` commands to focused implementation files. The generation-dependent schema helper builds only after bootstrap contracts exist. The [command map](../tools/README.md) explains their organization. Typed option/descriptor assertions live in the test project, rather than a reflection-based inspection script.

## Requirement coverage and observed evidence

The test methods below are organized into [catalog](../tests/Envoy.ControlPlane.Protocol.Tests/CatalogTests.cs), [annotation](../tests/Envoy.ControlPlane.Protocol.Tests/AnnotationTests.cs), [serialization](../tests/Envoy.ControlPlane.Protocol.Tests/SerializationTests.cs), [service-contract](../tests/Envoy.ControlPlane.Protocol.Tests/ServiceContractTests.cs) and [generator-compatibility](../tests/Envoy.ControlPlane.Protocol.Tests/GeneratorCompatibilityTests.cs) classes. Shared descriptor/annotation inspection and the ten-service fixture data live under [Support/](../tests/Envoy.ControlPlane.Protocol.Tests/Support/). One xUnit collection preserves the original suite's sequential execution while keeping all eight checks.

| Requirement | Exact evidence |
|---|---|
| V02: eight per-type discovery contracts, ADS and ratelimit configuration | `DiscoveryContracts_AllRequiredClientAndServerShapes_Compile` |
| V03: listener/HCM/router/TLS and ratelimit binary/Any/JSON graph | `CommonGraph_ListenerHcmRouterTlsAndRatelimit_RoundTripsBinaryAnyAndJson` |
| V03: external descriptor registration, unknown Any JSON rejection and wrong-type unpack | `ExternalAny_UpstreamPayload_RequiresRegistrationAndRoundTrips` using upstream `google.protobuf.Type` |
| V04: catalog completeness and one descriptor/CLR owner | `Catalog_SelectedInputs_HaveExactlyOneDescriptorAndClrOwner` |
| V04/V38: all source descriptors, JSON names and original options | `Descriptors_AllSelectedInputs_MatchPinnedSourceIncludingOptions` |
| V04: typed PGV values/definitions and inventory drift | `OriginalPgvOptions_TypedAccessAndDefinitions_RemainAvailable` |
| V03: unknown binary fields survive serialization | `Binary_UnknownFields_ArePreserved` |
| Generator corrections preserve names and oneof/reflection behavior | `GeneratorNameCorrections_PreserveDescriptorsAndOneofBehavior` |
| V37: local package restore/use with no source reference | `Bindings.cs verify` runs the same eight tests from the clean package consumer |
| V38: regeneration/configuration/output drift | `Bindings.cs generate` verifies source, Buf executable, configuration, descriptor and generated-file fingerprints |
| Source export selection/repetition and rejection before writes | `Export_ValidFixture_WritesOnlySelectedVerifiedInputs`; `Export_DriftedCatalog_RejectsBeforeWritingInputs` in the upstream CLI suite |

Observed on macOS arm64, 24 October 2026: Buf generation, locked build/pack, **8/8 project binding tests**, **8/8 clean-package binding tests** and **19/19 upstream CLI tests** passed, with no skips. The [workflow](../.github/workflows/upstream-verification.yml) registers both projects and the package proof for Linux/macOS/Windows; T02 remote CI results remain pending until publication.

This proves the candidate binding surface for the selected catalog. It does not establish every message/value serialization combination, Go runtime parity, validation equivalence or live Envoy interoperability. G1 remains open until T03. The package remains a local candidate and is not published to NuGet.
