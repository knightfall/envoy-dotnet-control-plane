# Architecture

Reviewed 24 October 2026. Source paths below are relative to go-control-plane/.

## Upstream flow

1. An application builds Envoy messages and publishes per-node snapshots or per-type resources.
2. Discovery handlers create watches or perform unary fetches; cache changes satisfy eligible watches.
3. SotW and Delta handlers manage separate subscription/version state. ADS multiplexes resource types.
4. Cache readiness and optional ordered ADS coordinate delivery; callbacks run in the corresponding request/response path.
5. HTTP gateway routes select a type URL and delegate to the shared fetch implementation.
6. A separate SotW ADS client wrapper receives resources and sends ACK/NACK based on received and accepted state.

SnapshotCache holds per-node versions, LinearCache manages a single collection, and MuxCache delegates using classification. Consistency checks are explicit and narrower than arbitrary graph validation. ADS does not create a cross-type atomic application transaction.

## Proposed .NET direction

Keep protocol engines/caches independent of ASP.NET Core adapters. Port Go's stream processing loop and per-type watches with source-equivalent callback sequencing. Feature availability stops at the selected root parity surface; include fetch/client/helper behavior in 1.0. Choose extra synchronization or delivery machinery only for a demonstrated porting need. [TODO] Finalize signatures after binding type ownership is selected.

The [detailed proposed architecture](../architecture/ARCHITECTURE.md) expands contracts, watch lifetimes, mode-specific nonce behavior, SotW heartbeats, and hosting. [Optional schema validation](../architecture/VALIDATION.md) is active scope: T03 checks feasibility before binding freeze and T26 has a separate package/conformance gate. The [backlog](../plan/IMPLEMENTATION-PLAN.md) defines planned work; only T27/T28 remain deferred.

## Evidence

- README.md:18-44,76-98
- pkg/cache/v3/simple.go:31-92,264-286; snapshot.go:83-121
- pkg/server/v3/server.go:44-84,186-357; gateway.go:38-98
- pkg/server/config/config.go:20-38; pkg/client/sotw/v3/client.go:39-90
- [xDS protocol](https://www.envoyproxy.io/docs/envoy/latest/api-docs/xds_protocol.html)
