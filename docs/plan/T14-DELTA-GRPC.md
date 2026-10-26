# T14 Delta gRPC adapters

The final T14 increment connects the merged [cache](T14-DELTA-CACHE.md) and [stream](T14-DELTA-STREAMS.md) work to the official generated gRPC contracts through ASP.NET Core/Kestrel. It adds no runtime dependency, schema, generator or descriptor change. Exact-head review/CI are pending; completed local results are recorded below.

## Service matrix

Adapters follow [pinned `pkg/server/v3/server.go:315–356`](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/v3/server.go#L315), using the same immutable behavior/schema pins as the earlier increments.

| Generated service | Delta RPC | Implied resource |
|---|---|---|
| AggregatedDiscoveryService | `DeltaAggregatedResources` | Request type required |
| ClusterDiscoveryService | `DeltaClusters` | Cluster |
| EndpointDiscoveryService | `DeltaEndpoints` | ClusterLoadAssignment |
| ListenerDiscoveryService | `DeltaListeners` | Listener |
| RouteDiscoveryService | `DeltaRoutes` | RouteConfiguration |
| ScopedRoutesDiscoveryService | `DeltaScopedRoutes` | ScopedRouteConfiguration |
| SecretDiscoveryService | `DeltaSecrets` | Secret |
| RuntimeDiscoveryService | `DeltaRuntime` | Runtime |
| ExtensionConfigDiscoveryService | `DeltaExtensionConfigs` | TypedExtensionConfig |
| VirtualHostDiscoveryService | `DeltaVirtualHosts` | VirtualHost |
| LocalityEndpointDiscoveryService | `DeltaLocalityEndpoints` | LbEndpoint |

The eight standard services and ADS retain their SotW methods; VHDS/LEDS have only Delta methods. The eleven mapped service descriptors contain **28 RPC declarations**: nine implemented SotW streams, eleven implemented Delta streams and eight generated Fetch methods still unimplemented. Rate-limit configuration hosting remains later work; its source descriptor has no dedicated Delta RPC. Arbitrary types remain possible through ADS/custom watchers.

## Hosting and ownership

`AddEnvoyControlPlane` registers separate singleton SotW/Delta engines and bridges, preserving application registrations through `TryAdd`. Register optional `IDeltaCallbacks` and `DeltaOptions` through normal DI. Each engine has independent server-local stream IDs and per-call sessions. Call cancellation and host `ApplicationStopping` reach the existing Core loop. The adapter owns neither an instance-registered application cache nor a client channel.

`MapEnvoyControlPlane` maps all eleven generated services and returns the same ordinary route group for authorization. The Delta bridge supplies `ServerCallContext` as open context state, forwards generated asynchronous readers/writers, translates Core `InvalidArgument`/`Unavailable`, preserves application `RpcException` status and logs lifecycle/status at debug level without payloads. Kestrel/TLS/message limits and graceful shutdown use normal host configuration. No new quota, cleanup timer, ordering mode or ACK policy is added.

## Requirement evidence

Tests use the existing ephemeral-loopback Kestrel host and generated clients, with explicit cache/callback channels rather than timing sleeps. Observed locally on macOS arm64: **53 hosting / 325 solution tests passed**, zero failed/skipped and zero compiler/Roslynator warnings/errors. All prior pinned Go corpora pass, including ten Delta stream traces, fourteen Delta cache cases and the injected SotW stale-association rejection. Five package/symbol pairs and fresh Protocol **14**, Validation **18**, Core **181**, Hosting **53** test consumers plus both executable graph consumers passed. Three existing non-packable-project notices remain. No performance baseline ran.

| Requirement | Exact test |
|---|---|
| All eleven RPCs: HTTP/2, implied/ADS type, exact named payload/hash, nonce, EOF/cleanup | `DeltaService_RoundTripsNamedPayloadAndVersionOverHttp2` — eleven rows above |
| Reconnect initial versions, changed resources, wildcard transition, NACK/old nonce, removal and per-stream nonce restart | `DeltaAds_InitialVersionsUpdatesRemovalsNackAndReconnectConverge` |
| Registered legacy wildcard options | `RegisteredDeltaOptions_ControlLegacyWildcard` |
| Missing ADS type, closed producer and application rejection status | `DeltaRejection_MapsStatusAndClosesTheCall` |
| Unauthenticated, forbidden and authorized Delta requests | `StandardHostAuthorization_AppliesToDeltaMethods` |
| Client cancellation and host stopping, watch cleanup/cache ownership, channel reuse | `Cancellation_ReleasesPendingDeltaWatchWithoutDisposingTheCache` |
| Complete declaration inventory; no rate-limit hosting | `EndpointInventory_ContainsSotwAndDeltaContractsWithoutRateLimitHosting` |
| Deferred unary Fetch remains unimplemented | `DeferredFetch_RetainsGeneratedUnimplementedStatus` |

LEDS tests use a small `INamedResource` wrapper to associate the discovery envelope name with the real generated LbEndpoint payload, which has no protobuf name field. This uses the existing resource naming contract; no synthetic wire message is introduced. The raw SnapshotCache prefix-selection and Delta TTL omissions remain the explicit pinned source limits in the cache evidence.

The historical [SotW preview acceptance](../preview/README.md) remains scoped to its recorded methods/released-Envoy graph. T14 establishes the bounded Core/HTTP2 Delta implementation; full released-Envoy Delta convergence is **V32/T23 full-root** work. Full interoperability, TTL scheduling, other caches, Fetch/gateway and optional-validator conformance remain separate tasks. The accepted performance baseline is manual-only and is not a merge gate.
