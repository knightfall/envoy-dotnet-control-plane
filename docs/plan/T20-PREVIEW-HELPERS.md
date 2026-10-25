# T20 preview — resource and reference helpers

Work for [preview issue #13](https://github.com/knightfall/envoy-dotnet-control-plane/issues/13). This is the preview subset of T20; message/Struct conversion and the full helper inventory remain in the separate 1.0 deliverable. Behavior reference: `67c5345367582741334e34ffc66a561b0ce32946`. Schema, generator and descriptor pins are unchanged.

## Source-to-C# mapping

| Upstream surface | Preview mapping and disposition |
|---|---|
| `resource.Type` string alias | Ordinary C# `string`; no wrapper type |
| `APITypePrefix`, v3 resource type constants and ADS empty type | Existing `ResourceTypeUrls`; includes all selected families, not only the four sample families |
| `DefaultAPIVersion` | Generated `Envoy.Config.Core.V3.ApiVersion.V3`; no duplicate alias |
| `GetResponseType`, `GetResponseTypeURL` | Existing `ResourceCatalog.GetResponseType` / `GetTypeUrl` |
| `GetResourceName`, `GetResourceNames` and name indexing | Existing `ResourceCatalog.GetName`, `GetNames`, `IndexResourcesByName`; generated descriptors/custom `INamedResource` supply .NET naming |
| `ResourceRequiresFullStateInSotw` | Existing `ResourceCatalog.RequiresFullStateInSotw` |
| `GetHTTPConnectionManager` | Existing `ResourceReferences.GetHttpConnectionManager`; typed protobuf unpacking, null for absent/unrelated/malformed configuration |
| EDS/RDS reference helpers | Existing `ResourceReferences.GetReferences` and explicit `Snapshot.EnsureConsistent` |
| `wellknown.HTTPConnectionManager`, `wellknown.Router` | Two canonical preview labels in `WellKnownFilterNames` |
| Remaining `wellknown` constants | Outside this preview subset; retain their inventory for the 1.0 decision |
| Eight Fetch URL constants | T18/full helper inventory, rather than unused preview endpoint APIs |
| Marshal/hash helpers | Existing `ResourceSerialization`; no Go vtprotobuf API clone or universal cross-runtime hash claim |
| Message/Struct conversion and JSON descriptor registry errors | Separate full T20 task; binary typed-Any extraction needs no custom registry |
| Go logging facade | T11 uses ordinary `ILogger`; no duplicate logging interface |

The pinned Go `wellknown` package is explicitly deprecated: extension selection uses the typed-config protobuf URL, and that package is no longer receiving new names. The two labels make the sample readable and retain familiar names; they do not become a registry or constrain extension identity. The shared cases include a different filter label and a custom valid type-URL prefix. Unknown custom types return no HCM instead of being silently decoded as one.

## Example graph

`ExampleResources` is an internal application helper shared with tests through focused source links. It constructs stable replacement CDS, EDS, RDS and LDS messages using the existing upstream bindings:

- Listener `listener` contains a typed HCM and typed router filter.
- HCM uses ADS/V3 RDS resource `routes`.
- The route targets EDS cluster `service`, whose ADS/V3 endpoint assignment is also `service`.
- Backend and listener ports come from the example's configuration; the publisher retains its application-owned update timer and awaits snapshot publication.

The sample explicitly calls `EnsureConsistent` before publication. This is an application choice; the cache still performs no automatic consistency or semantic validation. Construction moved out of the background service first, with an unchanged-behavior build, then the graph was extended. No production graph-builder abstraction, dependency, schema, descriptor owner or test-support package was introduced.

## Verification mapping

| Requirement | Concrete evidence |
|---|---|
| Names/types, HCM/router typed extensions, ADS/V3 sources and exact EDS/RDS references | `ExampleGraph_UsesTypedExtensionsAndResolvesExactEdsRdsReferences` |
| Stable published resources across backend/listener changes | `ReplacementGraph_RetainsPublishedEndpointAndListenerMessages` |
| Default/absent/unknown/malformed typed-Any behavior, custom label and URL prefix | `HelperCorpus_MatchesDeclaredTypedAnyOutcomesAndExportsExampleGraph` — eight independent declared cases |
| Example helper usage over real HTTP/2 ADS and all four family updates | `ExampleGraph_UpdatesAllFourFamiliesThroughAds` — both ordering settings, opaque versions/nonces, ACK/re-registration and cleanup |
| Pinned Go parity | Existing oracle extended with a `helpers` mode calling actual `GetHTTPConnectionManager`, well-known symbols, snapshot consistency and reference functions; eight declared cases, both labels/API version and the exported graph match the pinned Go functions |
| Clean package consumption | Focused sample source links are staged by the existing consumer copier; the complete Core (147) and Hosting (25) suites also pass against freshly packed packages |

Local macOS arm64 `repository verify` passes: 227 solution tests, zero failed/skipped, zero build warnings/errors with Roslynator; all five package/symbol pairs; fresh Protocol (12), Validation (18), Core (147) and Hosting (25) test consumers; executable Core/ASP.NET consumers. The Go helper comparison matches eight HCM/Any cases, both labels, API version, graph consistency/names/versions and exact references. Existing comparisons remain passing: 33 PGV, 19 snapshot, 22 cache, 14 subscription and 16 stream cases. The SDK pin is 10.0.400 and Go is 1.26.4. Exact-head remote CI and independent Astra medium review belong to the implementing PR. The earlier approval-service failure was resolved by a later approved retry; no check was bypassed. The static pairing helper remains unavailable due to its missing parser dependency; no static-pairing or percentage-coverage claim is made.

Released-Envoy bootstrap, accepted configuration, traffic and reconnect remain preview T23/T12 evidence. Native/helper/gRPC checks do not establish those outcomes. The accepted transport performance tradeoff is unchanged, and no package publication is authorized by this task.
