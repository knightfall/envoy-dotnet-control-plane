# T20 — resource and helper compatibility report

This completes issue #26 / R20 / V27 beyond the accepted preview helpers. Behavior source remains `67c5345367582741334e34ffc66a561b0ce32946`. The implementation adds no dependency, generator/schema changes, global descriptor registry or JSON codec. It reuses Google.Protobuf and the authoritative generated messages.

## Function and type inventory

The complete exported helper surface in `pkg/resource/v3/resource.go`, `pkg/cache/v3/resource.go`, `pkg/conversion/struct.go` and `pkg/wellknown/wellknown.go` is mapped below. C# names follow existing repository conventions. The earlier T05/T20 preview resource/reference behavior is retained.

| Upstream helper/type | .NET mapping or scoped adaptation |
|---|---|
| `resource.Type` | Ordinary `string`; no alias wrapper |
| `resource.DefaultAPIVersion` | Authoritative generated `ApiVersion.V3`; no duplicate enum |
| `resource.GetHTTPConnectionManager` | `ResourceReferences.GetHttpConnectionManager`, typed Any unpacking with null for absent/unrelated/malformed input |
| `cache.GetResponseType` | `ResourceCatalog.GetResponseType` |
| `cache.GetResponseTypeURL` | `ResourceCatalog.GetTypeUrl`; unknown enum throws ArgumentOutOfRangeException rather than returning a Go error tuple |
| `cache.GetResourceName` | `ResourceCatalog.GetName`; generated descriptors/custom INamedResource provide the .NET name contract |
| `cache.GetResourceNames` | `ResourceCatalog.GetNames`, preserving original sequence/null-name behavior |
| `cache.ResourceRequiresFullStateInSotw` | `ResourceCatalog.RequiresFullStateInSotw`, CDS/LDS only |
| `cache.GetResourceReferences` | `ResourceReferences.GetReferences(IEnumerable<ResourceEntry>)` |
| `cache.GetAllResourceReferences` | The same enumerable helper over resource groups; Snapshot.EnsureConsistent already aggregates its collections. No Go fixed-array/map facade is added |
| `cache.MarshalResource` | `ResourceSerialization.Serialize`, deterministic protobuf bytes; no vtprotobuf interface clone |
| `cache.HashResource` | `ResourceSerialization.ComputeHash(ReadOnlySpan<byte>)`, lowercase SHA256. This does not assert universal canonical protobuf bytes across runtimes |
| `conversion.MessageToStruct` | `MessageConversions.ToStruct` using stock protobuf JSON with preserved proto field names |
| `conversion.StructToMessage` | `MessageConversions.ToMessage<T>` / descriptor overload, returning a new message. It does not merge into a pre-populated caller destination |
| Name indexing/resource entry conveniences | Existing ResourceCatalog.IndexResourcesByName / ResourceCollection / ResourceEntry; no duplicate Go-shaped container surface |
| Go logging facade / printf functions | Ordinary application ILogger and the existing T11 host logs; no Core logging dependency or duplicate facade |
| Go marshal/size/clone fast-path interfaces | Official generated C# IMessage/IBufferMessage and normal protobuf APIs. Go technical interfaces are outside the managed API |

The last three rows are explicit .NET mappings/exclusions rather than additional upstream functions in those four files. They do not imply that generated service descriptors implement unrelated Envoy runtime services.

## Constant inventory

All **74 string symbols** (51 well-known names plus 23 type/prefix/Fetch strings) and the generated API-version value are compared to the imported pinned Go constants. The deprecated case-only source aliases collapse to **48** C# well-known members. Existing WellKnownFilterNames.HttpConnectionManager/Router remain compatible aliases. No Obsolete warning is added to break existing preview code.

The upstream wellknown package is deprecated and receives no new labels: typed-config protobuf URLs select extensions. These constants are conventional labels, not a registry, allowlist or constraint on type identity.

| Source symbol | .NET member |
|---|---|
| `wellknown.Buffer` | `WellKnownNames.Buffer` |
| `wellknown.CORS` | `WellKnownNames.Cors` |
| `wellknown.Dynamo` | `WellKnownNames.Dynamo` |
| `wellknown.Fault` | `WellKnownNames.Fault` |
| `wellknown.GRPCHTTP1Bridge` | `WellKnownNames.GrpcHttp1Bridge` |
| `wellknown.GRPCJSONTranscoder` | `WellKnownNames.GrpcJsonTranscoder` |
| `wellknown.GRPCWeb` | `WellKnownNames.GrpcWeb` |
| `wellknown.Gzip` | `WellKnownNames.Gzip` |
| `wellknown.IPTagging` | `WellKnownNames.IpTagging` |
| `wellknown.HTTPRateLimit` | `WellKnownNames.HttpRateLimit` |
| `wellknown.Router` | `WellKnownNames.Router` |
| `wellknown.HealthCheck` | `WellKnownNames.HealthCheck` |
| `wellknown.Lua` | `WellKnownNames.Lua` |
| `wellknown.Squash` | `WellKnownNames.Squash` |
| `wellknown.HTTPExternalAuthorization` | `WellKnownNames.HttpExternalAuthorization` |
| `wellknown.HTTPRoleBasedAccessControl` | `WellKnownNames.HttpRoleBasedAccessControl` |
| `wellknown.HTTPGRPCStats` | `WellKnownNames.HttpGrpcStats` |
| `wellknown.HTTPWasm` | `WellKnownNames.HttpWasm` |
| `wellknown.ClientSSLAuth` | `WellKnownNames.ClientSslAuth` |
| `wellknown.Echo` | `WellKnownNames.Echo` |
| `wellknown.HTTPConnectionManager` | `WellKnownNames.HttpConnectionManager` |
| `wellknown.TCPProxy` | `WellKnownNames.TcpProxy` |
| `wellknown.RateLimit` | `WellKnownNames.RateLimit` |
| `wellknown.MongoProxy` | `WellKnownNames.MongoProxy` |
| `wellknown.ThriftProxy` | `WellKnownNames.ThriftProxy` |
| `wellknown.RedisProxy` | `WellKnownNames.RedisProxy` |
| `wellknown.MySQLProxy` | `WellKnownNames.MySqlProxy` |
| `wellknown.ExternalAuthorization` | `WellKnownNames.ExternalAuthorization` |
| `wellknown.RoleBasedAccessControl` | `WellKnownNames.RoleBasedAccessControl` |
| `wellknown.OriginalDestination` | `WellKnownNames.OriginalDestination` |
| `wellknown.ProxyProtocol` | `WellKnownNames.ProxyProtocol` |
| `wellknown.TlsInspector` | `WellKnownNames.TlsInspector` |
| `wellknown.TLSInspector` | `WellKnownNames.TlsInspector` |
| `wellknown.HttpInspector` | `WellKnownNames.HttpInspector` |
| `wellknown.HTTPInspector` | `WellKnownNames.HttpInspector` |
| `wellknown.OriginalSource` | `WellKnownNames.OriginalSource` |
| `wellknown.Lightstep` | `WellKnownNames.Lightstep` |
| `wellknown.Zipkin` | `WellKnownNames.Zipkin` |
| `wellknown.DynamicOT` | `WellKnownNames.DynamicOt` |
| `wellknown.Datadog` | `WellKnownNames.Datadog` |
| `wellknown.Statsd` | `WellKnownNames.Statsd` |
| `wellknown.DogStatsd` | `WellKnownNames.DogStatsd` |
| `wellknown.MetricsService` | `WellKnownNames.MetricsService` |
| `wellknown.FileAccessLog` | `WellKnownNames.FileAccessLog` |
| `wellknown.HTTPGRPCAccessLog` | `WellKnownNames.HttpGrpcAccessLog` |
| `wellknown.TransportSocketAlts` | `WellKnownNames.TransportSocketAlts` |
| `wellknown.TransportSocketTap` | `WellKnownNames.TransportSocketTap` |
| `wellknown.TransportSocketRawBuffer` | `WellKnownNames.TransportSocketRawBuffer` |
| `wellknown.TransportSocketTls` | `WellKnownNames.TransportSocketTls` |
| `wellknown.TransportSocketTLS` | `WellKnownNames.TransportSocketTls` |
| `wellknown.TransportSocketQuic` | `WellKnownNames.TransportSocketQuic` |
| `resource.APITypePrefix` | `ResourceTypeUrls.Prefix` |
| `resource.EndpointType` | `ResourceTypeUrls.Endpoint` |
| `resource.ClusterType` | `ResourceTypeUrls.Cluster` |
| `resource.RouteType` | `ResourceTypeUrls.Route` |
| `resource.ScopedRouteType` | `ResourceTypeUrls.ScopedRoute` |
| `resource.VirtualHostType` | `ResourceTypeUrls.VirtualHost` |
| `resource.ListenerType` | `ResourceTypeUrls.Listener` |
| `resource.FilterChainType` | `ResourceTypeUrls.FilterChain` |
| `resource.SecretType` | `ResourceTypeUrls.Secret` |
| `resource.ExtensionConfigType` | `ResourceTypeUrls.ExtensionConfig` |
| `resource.RuntimeType` | `ResourceTypeUrls.Runtime` |
| `resource.ThriftRouteType` | `ResourceTypeUrls.ThriftRoute` |
| `resource.RateLimitConfigType` | `ResourceTypeUrls.RateLimitConfig` |
| `resource.LbEndpointType` | `ResourceTypeUrls.LbEndpoint` |
| `resource.AnyType` | `ResourceTypeUrls.Any` |
| `resource.FetchEndpoints` | `ResourceFetchPaths.Endpoints` |
| `resource.FetchClusters` | `ResourceFetchPaths.Clusters` |
| `resource.FetchListeners` | `ResourceFetchPaths.Listeners` |
| `resource.FetchRoutes` | `ResourceFetchPaths.Routes` |
| `resource.FetchScopedRoutes` | `ResourceFetchPaths.ScopedRoutes` |
| `resource.FetchSecrets` | `ResourceFetchPaths.Secrets` |
| `resource.FetchRuntimes` | `ResourceFetchPaths.Runtime` |
| `resource.FetchExtensionConfigs` | `ResourceFetchPaths.ExtensionConfigs` |

ResourceFetchPaths is reused by the existing HTTP gateway. It supplies exactly eight paths; no rate-limit/ADS/VHDS/LEDS route or behavior change is added.

## Conversion contract and boundaries

Conversions format/parse standard protobuf JSON, preserving proto field names, enum names, scalar representation and field presence. Returning a new destination is the .NET adaptation of the source's reset-and-unmarshal output parameter; no in-place Merge API with residual fields is introduced. Inputs are not mutated. Generic destinations use their generated type; descriptor destinations require an authoritative CLR parser. Map-entry descriptors without one fail explicitly. Null arguments produce ArgumentNullException; normal parser/formatter errors propagate.

Any payloads use an explicit application TypeRegistry. The helper adds no global reflection discovery or alternate descriptor owner. A complete registry can include arbitrary authoritative message descriptors, including Google.Rpc.Status and the example HCM/router types. Unknown/malformed payloads fail; missing known extension registration is a documented .NET configuration error. The Go oracle's global registry is populated by its imported generated packages. Scalar/array JSON roots from Duration/ListValue cannot become a Struct, matching the source; Struct object roots work. Runtime parser limits/exception types and protobuf binary map ordering are runtime boundaries, not byte-level equivalence claims.

## Requirement-to-test mapping

| V27 area | Exact native evidence |
|---|---|
| Message/Struct/default/presence/enum/uint64/proto names | MessageConversionsTests.SourceCases_DeclareExpectedOutcomesAndExportTheCompleteSymbolInventory (17 declared cases); RequestRoundTrip_UsesProtoNamesAndKeepsOriginalMessagesIndependent; DefaultsAndPresence_RetainTheProtobufJsonContract |
| Existing example, registered HCM/router Any, missing registration and reference preservation | ExistingExampleGraph_RoundTripsRegisteredHcmAndRouterAny; existing PreviewResourceTests.ExampleGraph_UsesTypedExtensionsAndResolvesExactEdsRdsReferences and Hosting PreviewGraphTests.ExampleGraph_UpdatesAllFourFamiliesThroughAds |
| Unknown/malformed Any, unknown/wrong field types | UnknownMalformedAny_AndInvalidObjectFieldsAreRejected; source corpus above |
| Null/default descriptor, unsupported CLR parser and WKT object boundary | NullArgumentsAndDescriptorsWithoutClrParsers_HaveExplicitFailures; ScalarAndArrayRoots_CannotBeRepresentedAsStruct |
| Complete string inventory / aliases / generated API version | SourceCases_DeclareExpectedOutcomesAndExportTheCompleteSymbolInventory and actual Go constant comparison |
| Retained names/types/full-state/index/reference and serialization behavior | Existing ResourceCatalogTests.TypeMappings_FollowUpstreamOrder, Names_EndpointAndGeneratedAndCustomMessages_MatchSource, UnknownNames_AndTypeUrls_HaveSourceOutcomes, SotwFullState_IsLimitedToClustersAndListeners; ResourceReferencesTests.EdsReferences_UseServiceOverrideOrClusterName, NonEdsAndRouteClusterReferences_AreIgnored, ListenerChains_IncludeRdsDefaultAndScopedListReferences, ScopedReferences_IncludeEmptyRouteName, MalformedAndUnrelatedAny_AreIgnored; ResourceSerializationTests.DeterministicMapOrder_ProducesSameBytesAndHash, UnknownFields_AffectVersionAndSurviveSerialization, EmptyResource_UsesKnownSha256Digest |

The new tests reuse existing authoritative messages and the example graph; no synthetic contract or new test project is added. The static source-pairing report is a heuristic, not coverage evidence. Tests cover the distinct conversion partitions directly without concurrency/race machinery.

## Source reuse and observed proof

Apache-2.0 attribution (Copyright 2018 Envoyproxy Authors) and the original packaged license are retained. Actual pinned file SHA256 values:

| Source file | SHA256 |
|---|---|
| pkg/resource/v3/resource.go | `79b321857f08c716e75c7e414d46a888753a1ab37c030a5d2bcc12615fa65b02` |
| pkg/cache/v3/resource.go | `2661839c2bc643ae451619a05edf7bf7a29c6a6063363dd5e91b93df8c39e639` |
| pkg/conversion/struct.go | `8b0cf0e1120546fa51ee0bbcbd27cf895cb775eda7d2f1bc35cc1c53f39269f1` |
| pkg/wellknown/wellknown.go | `dcced6b187a46afa3fed644eaa382cb6194ebd281a24295864b6c2e274524ac7` |

GoOracle/conversions.go imports the actual conversion functions and constants, adapting TestConversion's assertions to recorded observations. Seventeen independently specified native cases compare semantic Struct JSON and decoded protobuf message equality, including enum/presence/uint64/Any and error stages. Map wire ordering and JSON formatting are normalized through official parsing; field values/types/presence remain exact. No conversion/parser/type catalog algorithm is copied into the driver. All 74 constant names/values and API version must match exactly; omitted/duplicate/unknown names fail. repository verify retains reports under obj/upstream/results/conversions/.

Focused local proof: seven native tests passed and all 17 conversion/74 constant comparisons matched actual Go. Full local macOS arm64 `repository verify` passed **456 tests, 0 failed/skipped** (Core 261, hosting 104, Protocol 14, optional validation 18, tooling 59), compiler/Roslynator **0 warnings/errors**, all existing Go corpora plus 17 conversions/74 symbols, five package/symbol pairs, fresh consumers (14/18/261/104 tests) and both executable package graphs. Three existing non-packable-project notices remain. An altered Struct field was specifically rejected with `request: conversion differs`. SDK 10.0.400 / net10.0 / Go 1.26.4. Exact-head GitHub CI/Codex PR review remain pending merge gates. Full T21/T22/T23/T26 acceptance remains separate; the performance baseline stays manual and no packages are published.
