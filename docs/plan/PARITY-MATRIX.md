# v3 parity matrix

Reviewed 27 October 2026 against Go commit 67c5345367582741334e34ffc66a561b0ce32946. Paths below refer to the pinned remote source tree. This table defines the complete selected root surface. The [root compatibility report](../compatibility/root-1.0.md) maps its implementation, observed evidence and release limits. The [preview guide](../preview/README.md) and [preview manifest](../preview/compatibility.json) retain the historical SotW milestone.

## Discovery transports

The SotW versus Delta choice and the separate-stream versus ADS choice are independent. HTTP gateway support is distinct from unary gRPC Fetch support.

| Service/resource family | SotW stream | Delta stream | Unary gRPC Fetch | HTTP gateway route |
|---|---|---|---|---|
| ADS | Yes | Yes | None | None |
| CDS, EDS, LDS, RDS, SRDS, SDS, RTDS, ECDS | Yes | Yes | Yes | Yes |
| Virtual hosts / VHDS | None | Yes | None | None |
| Locality endpoints / LEDS | None | Yes | None | None |
| Rate-limit configuration discovery | Yes | No dedicated Delta RPC | Yes | None |

The rate-limit service descriptor declares only StreamRlsConfigs and FetchRlsConfigs. ADS has generic type-URL dispatch; the absence of a dedicated RPC does not mean an arbitrary type cannot travel over ADS.

HTTP paths are the eight constants in pkg/resource/v3/resource.go, implemented by pkg/server/v3/gateway.go. Preserve path-to-type-URL selection and the existing unchanged-result 304, invalid-input 400, unknown-route 404, and fetch-error 500 behavior, subject to explicitly documented host-layer limits.

Current implementation: ten SotW methods (including rate-limit), all eleven Delta methods and all nine unary Fetch methods have generated-client HTTP/2 evidence; see [T14](T14-DELTA-GRPC.md) and [T18](T18-FETCH-GATEWAY.md). The eight-route HTTP gateway has native transport and pinned-Go JSON/status evidence. [T23](T23-ROOT-ENVOY.md) separately proves the advertised four-family graph on released Envoy; it does not run every proxy workload.

All implemented methods above belong in recommended 1.0 scope. The preview covers the eight standard SotW per-type services and SotW ADS with SnapshotCache; ratelimit configuration joins the complete 1.0 surface. The Go reference does not supply runtime implementations for every other generated Envoy service, such as external authorization, rate-limit decisions, or load reporting.

Evidence: pkg/server/v3/server.go:44-61,186-357; pkg/server/v3/gateway.go:38-98; pkg/resource/v3/resource.go:41-52; ratelimit/service/ratelimit/v3/rls_conf_ds.pb.go:172-174,237-253.

## Root-library behaviors

| Upstream surface | Required behavior | Release |
|---|---|---|
| Snapshot, Resources, ResourceWithTTL | Resource names/type URLs, opaque per-type versions, partial snapshots, optional source-matching consistency checks, lazy Delta version maps | Preview primitives; Delta/TTL completed for 1.0 |
| ResourceSnapshot / ConfigWatcher / ConfigFetcher | Consumer extension points; custom snapshots and watches | Preview interfaces; all modes for 1.0 |
| SnapshotCache / node hashing | Per-node publication/get/clear; watches; ADS readiness; partial requests; status | Preview; all Delta/TTL cases for 1.0 |
| LinearCache | Per-resource changes, version prefix, named/prefix/wildcard watches; Fetch returns its upstream unsupported-operation error | 1.0 |
| MuxCache | Separate SotW/Delta classification, delegated Fetch, missing-key errors | 1.0 |
| Stream options | Ordered ADS option and default false; legacy wildcard enabled by default, global/per-type deactivation | 1.0; relevant SotW defaults in preview |
| Callbacks | Open/request/response/close order; error rejection; node identity; in-path completion | Both |
| Cache status | Node identity, watch counts, last request times | Both |
| TTL / heartbeats | SotW TTL wrappers and caller-owned heartbeat loop; raw Delta omits TTL like the source, while explicit pass-through Delta envelopes may carry TTL; cancellation/disposal | Implemented for 1.0; [T17](T17-TTL-HEARTBEATS.md) and released-proxy limitation in [T23](T23-ROOT-ENVOY.md) |
| Fetch handlers | Unary service adapters and common cache-fetch semantics | 1.0 |
| HTTPGateway | Eight routes; Protobuf JSON/Any; status behavior | 1.0 |
| SotW ADSClient | Initialization, initial request, receive, ACK/NACK based on last accepted/received state, connection-error classification | Implemented; [T19 evidence](T19-SOTW-ADS-CLIENT.md) |
| Resource helpers / wellknown | Type URLs/names, reference extraction including HCM Any, relevant well-known extension names | Both |
| Struct conversion | Message/Struct conversion using Protobuf JSON rules | 1.0 |
| Logging | Map the Go facade to ILogger; document the .NET API change | Both |
| Examples/test support | Reusable fixtures and a minimal host/example; no production test-support package required | Both |

Evidence: pkg/cache/types/types.go; pkg/cache/v3/cache.go; simple.go; snapshot.go; linear.go; mux.go; status.go; pkg/server/config/config.go; pkg/server/sotw/v3/server.go; pkg/client/sotw/v3/client.go; pkg/conversion/struct.go; pkg/resource/v3/resource.go; pkg/wellknown/wellknown.go.

## Generated APIs and separate modules

| Area | Recommendation | Compatibility statement |
|---|---|---|
| Standard Envoy schema catalog | Pinned generated package including usable extension messages and required imports | [Catalog](../../schemas/catalog.json), [binding proof](../../schemas/BINDINGS.md) and clean package consumers establish the selected compiled surface |
| Discovery envelope dependencies only | Insufficient by themselves for typical typed configuration | Any hides extension references from the static envelope import graph |
| Custom resources/extensions | IMessage/Any and descriptor registration extension points | Do not require modifying the core assembly for every consumer extension |
| ratelimit schema module | Configuration/discovery types and root methods are implemented | [Source lock](../../schemas/upstream.lock.json) pins original schemas; descriptor/consumer and T18 transport evidence remain distinct |
| contrib and legacy API catalogs | Deferred; explicit scope decision required | No v2 control-plane runtime in the current root library |
| Schema validation metadata | Preserve original upstream options and definitions from the first binding release | T02/T03 prove accessibility and one descriptor owner before G1 |
| Optional explicit semantic validation | T03 source-built Protovalidate feasibility is implemented; full T26 conformance remains open | Bounded 33-case research is not GV; declared rule coverage still requires conformance, with no Core evaluator dependency or publication decorator |
| PGV Validate/ValidateAll API shape | Exact method shape/error text/order is not part of semantic parity by default | Standard C# generation does not recreate validators; claim only outcomes and diagnostics proven at GV |
| vtprotobuf | No C# API clone | Go-specific generated performance API; compare .NET behavior/performance instead |
| xdsmatcher | Deferred; explicit scope decision required | Match-tree evaluation, registry, legacy conversion need their own cases |

## Behavioral caveats

The [detailed architecture](../architecture/ARCHITECTURE.md) describes state/lifetime contracts. Every surface maps to the [backlog](IMPLEMENTATION-PLAN.md) and [verification gates](TRACEABILITY.md). T03 feasibility gates binding freeze; T26/GV separately gates the optional validation package and its claims. Matcher and broader catalogs remain T27/T28 deferred scope. The [validation plan](../architecture/VALIDATION.md) defines original metadata preservation, rule equivalence, and per-update coverage checks.

- Snapshot.Consistent is optional and checks the source's endpoint/route reference sets. SetSnapshot stores and publishes without invoking it automatically.
- SotW does not mean every resource type always sends an entire global snapshot. Match type-specific behavior, notably cluster/listener full-state responses.
- ADS readiness and ordering do not provide a cross-type atomic application transaction.
- Preserve wildcard option defaults and transitions; do not enable ordered ADS by default just because the option exists.
- Preserve mode-specific nonce/callback behavior. Delta's reviewed loop does not use SotW's stale-nonce rejection or an ACK-committed version ledger.
- The caller-owned SnapshotCache heartbeat loop scans SotW watches; raw Delta TTL omission is source-matched. Explicit Delta envelopes do not imply an automatic Delta scheduler. The default released Envoy SotW CDS mux has the [documented payload-free heartbeat limitation](T23-ROOT-ENVOY.md).
- Compare nonce and version relationships; different concrete opaque strings can be valid. Do not require universal byte-for-byte protobuf serialization or cross-language hash identity.
- LinearCache's unsupported Fetch remains an observable limitation; extending it requires a separate scope decision.
- Protocol schema support and released Envoy runtime compatibility need separate manifests and evidence.

Evidence: pkg/cache/v3/snapshot.go:83-121; pkg/cache/v3/simple.go:264-286; pkg/cache/v3/resource.go:111-124,145-147,300-305; pkg/cache/v3/linear.go:653-655; pkg/server/config/config.go:20-38; [Envoy xDS specification](https://www.envoyproxy.io/docs/envoy/latest/api-docs/xds_protocol.html); [Protobuf serialization](https://protobuf.dev/programming-guides/serialization-not-canonical/).

The [T20 helper compatibility report](T20-HELPER-COMPATIBILITY.md) maps every selected resource/name/reference/conversion/well-known helper, all constant symbols and the explicit .NET API/logging/marshal adaptations.
