# T05 — Resource entries and snapshots

Implemented 25 October 2026 against behavior baseline `67c5345367582741334e34ffc66a561b0ce32946`. T04 merged in PR #38; T05 implements Core resource primitives without a cache, stream engine or hosting adapter. Those remain T06 onward. This evidence covers the stable-resource convention and V03–V07 portions relevant to snapshots; it does not establish live Envoy interoperability or full optional-validator conformance.

## Source contract

The reviewed upstream files are [resource-family definitions](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/types/types.go), [type URLs](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/resource/v3/resource.go), [resource collections](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/resources.go), [names, references and serialization](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/resource.go), [snapshots](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/snapshot.go) and the [snapshot interface](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/simple.go). Corresponding resource, resources and snapshot tests informed the independent fixtures. Adapted algorithms retain source attribution; Core packages include the upstream Apache-2.0 license.

## Implemented surface

Production code lives in [Core Resources](../../src/Envoy.ControlPlane/Resources/); [the package readme](../../src/Envoy.ControlPlane/README.md) contains a minimal usage example.

| API | Behavior |
|---|---|
| `ResponseType`, `ResourceTypeUrls`, `ResourceCatalog` | Twelve families in upstream order, known/unknown type mapping, special endpoint naming, generated/custom names, raw indexing and Cluster/Listener SotW full-state classification |
| `ResourceEntry`, `ResourceCollection` | Retained protobuf message and optional TTL, opaque version, ordinal name index, last duplicate wins |
| `Snapshot`, `IResourceSnapshot` | Partial known-family snapshots, independent collection versions, explicit absence, application-defined snapshot interface for arbitrary types |
| `ResourceReferences`, `Snapshot.EnsureConsistent` | Explicit EDS/RDS reference extraction and exact resource-name-set checks |
| `ResourceSerialization`, `Snapshot.ConstructVersionMap` | Deterministic protobuf bytes and cached SHA256 resource versions, requested explicitly |

No schema source, generated binding correction, generator pin or evaluator source changed. No additional runtime package dependency was introduced. Core depends on Protocol; optional validation and hosting remain separate.

## Lifetime, absence and version semantics

Callers keep retained messages and resource collections stable and publish replacement objects for updates. Snapshot construction retains messages; no mandatory cloning, ownership transfer, mutation isolation or new concurrency guarantee is introduced. `GetResources` copies the outer dictionary while retaining message references. `GetResourcesAndTtl` returns the retained dictionary through a read-only interface. A consumer must not cast and mutate that retained dictionary.

Construction rejects unknown type URLs in the default snapshot. Querying an absent or unknown family returns null resources and an empty type version. An explicitly supplied empty group retains its version. Known type URLs do not trigger message-class validation. Custom families use an application implementation of `IResourceSnapshot`; descriptor registration and Any parsing use the protobuf runtime's application-owned `TypeRegistry` rather than a new global registry.

Type versions are opaque equality tokens; they need not increase numerically. The collection-based constructor accepts an independent version for each family. Resource versions use deterministic protobuf bytes and lowercase SHA256; an unchanged payload has the same resource version regardless of TTL or type version. Unknown binary fields survive serialization and affect the hash. Literal hashes are not promised to match another protobuf runtime across languages.

`ConstructVersionMap` is explicit and caches its first result. Reading resources or constructing the snapshot does not serialize messages. Once constructed, known absent families have empty resource-version maps; unknown families remain unavailable. Response-envelope lazy serialization belongs to later cache/stream tasks.

## Consistency boundary

Construction invokes neither consistency nor semantic validation. `EnsureConsistent` checks only the source's Endpoint and Route name sets. It follows EDS clusters (service-name override or cluster-name fallback), listener normal/default filter chains, standalone filter chains, RDS HCM configuration, inline scoped-route lists, and scoped-route resources. Scoped-route empty names are retained. Malformed or unrelated HCM Any payloads are ignored as upstream does. Static cluster resources and route-to-cluster references do not add EDS/RDS requirements.

Missing, extra and wrong-name Endpoint/Route resources fail the explicit check. Partial snapshots without such unresolved references remain usable. Protobuf validation, ACK/NACK behavior and acceptance by a running Envoy proxy are separate checks.

## Verification mapping

The [Core project](../../tests/Envoy.ControlPlane.Tests/Envoy.ControlPlane.Tests.csproj) uses the repository's xUnit v3/native Microsoft Testing Platform setup, central package management, locked dependencies and Roslynator. Its 30 test methods expand to 38 native cases. Assertions are based on declared outcomes, retained-object identity, independent wire bytes, a known SHA256 digest and meaningful equality/change relationships.

| Requirement | Representative tests |
|---|---|
| V03 names, type families and custom naming | `TypeMappings_FollowUpstreamOrder`, `Names_EndpointAndGeneratedAndCustomMessages_MatchSource`, `UnknownNames_AndTypeUrls_HaveSourceOutcomes`, `SotwFullState_IsLimitedToClustersAndListeners` |
| V03 TTL/index behavior | `Indexing_RetainsMessageAndTtl_LastDuplicateWins`, `ResourceWithoutTtl_PreservesAbsence`, `RawIndexing_RetainsLastDuplicateAndUnnamedResources` |
| V04 application descriptor ownership/custom snapshots | `ApplicationTypeRegistry_RoundTripsCustomAnyWithoutGlobalRegistration`, `CustomSnapshot_ExposesArbitraryTypeThroughInterface` |
| V05 stable lifetimes and explicit updates | `Messages_AreRetained_AndReplacementSnapshotIsIndependent`, `ResourcesWithoutTtl_AreNewIndexes_RetainingMessages` |
| V06 lazy, opaque and deterministic versions | `ResourceGroups_RetainIndependentOpaqueVersions`, `VersionMap_IsExplicitAndCached`, `VersionMap_SamePayload_IgnoresCallerVersionAndTtl`, `DeterministicMapOrder_ProducesSameBytesAndHash`, `UnknownFields_AffectVersionAndSurviveSerialization`, `EmptyResource_UsesKnownSha256Digest` |
| V07 absence and explicit consistency | `PartialSnapshot_DistinguishesAbsentAndExplicitEmptyGroups`, `UnknownType_ConstructionRejectsAndQueriesReturnAbsent`, `KnownType_DoesNotRequireMatchingMessageClass`, `Consistency_ChecksExactEndpointSets`, `Consistency_ChecksExactRouteSets`, `Constructor_DoesNotCheckConsistencyOrEvaluateRules` |
| V07 reference sources/Any | `EdsReferences_UseServiceOverrideOrClusterName`, `NonEdsAndRouteClusterReferences_AreIgnored`, `ListenerChains_IncludeRdsDefaultAndScopedListReferences`, `ScopedReferences_IncludeEmptyRouteName`, `MalformedAndUnrelatedAny_AreIgnored` |
| Independent source comparison | `Corpus_SourceDerivedFixtures_MatchDeclaredOutcomes` plus [Go oracle](../../tests/resources/README.md) |

The 19-case JSON corpus declares expected names, opaque versions and consistency independently of the implementation. The Go executable reconstructs its protobuf wire messages and compares actual Go names, versions, TTL presence/value and consistency against the .NET report. It checks lazy version-map availability and SHA256 shape without claiming cross-language literal-hash equality. Both Go modules resolve to the pinned behavior revision; replacements or changed pins fail repository verification.

Static review considered changes such as keeping the first duplicate, merging absent and empty groups, using a constant hash, hashing TTL/type versions, eagerly serializing, omitting default/scoped references, accepting wrong-name resources and validating during construction. The corresponding assertions distinguish those behaviors. This was a source/assertion review, not an executed mutation-testing result. T05 contains no timing-dependent race tests or speculative cancellation framework.

## Observed local result

On macOS arm64 with .NET SDK **10.0.400**, Go **1.26.4**, Buf **1.75.0** and existing verified generated bindings:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify
```

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Complete solution, eleven maintained projects | 92 | 0 | 0 |
| New Core suite, included above | 38 | 0 | 0 |
| Independent Go snapshot cases | 19 | 0 | 0 |
| Existing Go PGV comparison cases | 33 | 0 | 0 |
| Fresh Protocol package suite | 12 | 0 | 0 |
| Fresh Validation package suite | 18 | 0 | 0 |
| Fresh Core package suite | 38 | 0 | 0 |

Solution build completed with **0 warnings and 0 errors**. All five candidate packages and five symbol packages passed content inspection. Separate Core and ASP.NET executable consumers passed dependency/framework checks; Core also constructed and checked a snapshot, while ASP.NET built/disposed its host without listening. The full Core test suite ran from the candidate package with no source project reference and a fresh NuGet cache.

The Linux/macOS/Windows workflow runs the same generation and repository verification entry points. Remote CI results belong to the implementing PR and are distinct from these local observations. Candidate packaging does not publish to NuGet.org.

## Limits

- TTLs use `TimeSpan` with 100 ns precision; Go duration can represent 1 ns. The comparison fixtures use exactly representable values and preserve absent versus zero.
- Custom naming uses `INamedResource` or the generated protobuf string `name` field, adapting Go's structural `GetName` interface to .NET. It does not create another descriptor owner.
- Version-map construction assumes stable resources and the source's lifecycle; concurrent initialization or mutation isolation is not a new public guarantee.
- The default family set is intentionally bounded. Application snapshots can expose other type URLs, but no arbitrary-family cache or network delivery is claimed yet.
- This completes the snapshot portion of resource primitives. Watches, caches, transport responses, TTL delivery, streaming state machines and live Envoy tests remain later tasks.
