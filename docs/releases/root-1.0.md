# Root 1.0 release preparation

T25 prepared the root candidate and its review evidence. The optional evaluator passed its separate T26 declared-scope gate in PR #73 and retains its separate dependency graph and documented exceptions. All five packages were subsequently published as [1.0.0-rc.1](https://github.com/knightfall/envoy-dotnet-control-plane/releases/tag/v1.0.0-rc.1); [issue #74](https://github.com/knightfall/envoy-dotnet-control-plane/issues/74) records the protected publication and verification evidence.

## Candidate identity and version

The owner selected the `Knightfall.Envoy.ControlPlane` NuGet family. Assembly names, C# namespaces and protobuf identities retain their existing names. All packages target net10.0.

| Package ID | Version | Release scope |
|---|---|---|
| Knightfall.Envoy.ControlPlane.Protocol | 1.0.0-rc.1 | Root candidate |
| Knightfall.Envoy.ControlPlane.Validation.Annotations | 1.0.0-rc.1 | Root candidate, annotations only |
| Knightfall.Envoy.ControlPlane | 1.0.0-rc.1 | Root candidate |
| Knightfall.Envoy.ControlPlane.AspNetCore | 1.0.0-rc.1 | Root candidate |
| Knightfall.Envoy.ControlPlane.Validation | 1.0.0-rc.1 | Explicit optional evaluator; T26 declared-scope conformance |

The distinct prefix avoids reusing the existing public `Envoy.ControlPlane` ID. Publication confirmed the selected package identities and publishing rights for this release. Package-content and executable consumer checks verify new IDs/versions with unchanged assembly names and dependency ownership.

## Candidate release notes

The root candidate adds the complete selected v3 server/cache/helper surface over the historical SotW preview: Delta subscriptions/cache/streams and eleven adapters; LinearCache and MuxCache; TTL response envelopes and explicit SotW heartbeat lifecycle; all nine Fetch methods and eight optional HTTP routes; rate-limit configuration SotW; the focused SotW ADS client; complete selected resource/reference/Struct/conventional-name helpers. All thirty selected gRPC methods have generated-client HTTP/2 proof.

The implementation uses idiomatic C# channel delivery, asynchronous publication/disposal, cancellation tokens, `TimeProvider` and ordinary ASP.NET Core DI/authorization/logging. Original PGV metadata and authoritative generated message ownership remain intact. Core and hosting contain no evaluator/CEL dependency. See [usage/migration](../usage.md), [public API contracts](../public-api.md) and [compatibility](../compatibility/root-1.0.md) before upgrading.

Known limits include stable caller-owned messages, source SnapshotCache prefix/readiness behavior, raw Delta TTL omission, unsupported Linear Fetch, application-owned reconnect/policy and explicit opaque-Any registration. The default Envoy 1.40.1 SotW CDS mux removes clusters on payload-free heartbeats; the separately proven explicit Delta TTL path does not add an automatic scheduler. The proxy matrix covers one released version on Linux amd64/arm64 and a four-family graph. The complete selected catalog is broader than that live graph. Broader contrib/legacy catalogs and standalone matcher remain deferred. Optional validation is limited to the pinned T26 catalog/rule set and its named exceptions.

## Engineering checklist

- [x] Reconcile the implemented service/cache/helper matrix, necessary C# adaptations and optional validation boundary in the root compatibility report.
- [x] Record public API defaults, ownership, cancellation, error and migration contracts with links to their implementation/evidence.
- [x] Select the owner-approved NuGet prefix and set four coherent root candidate package versions without changing assembly/protobuf identities. Confirm actual publishing rights only before authorized publication.
- [x] Locally build/pack the concrete candidate, inspect IDs/versions/retained assemblies/symbols/docs/notices/dependency ownership, and run fresh package-consumer suites and both executable graphs. Exact committed-head CI remains below.
- [x] Locally verify unchanged binding/descriptor fingerprints and run required source/native/lifecycle/update checks against the candidate working tree; retain its dirty-tree status accurately.
- [x] Record Linux/macOS/Windows and Linux amd64/arm64 proxy results for runtime/package commit `eebfe5783cae67c840f29c86a1a347f83962402e`; link immutable workflow runs and retain scope limits in the compatibility report.

Final T25 signoff is recorded in [PR #72](https://github.com/knightfall/envoy-dotnet-control-plane/pull/72) and [issue #31](https://github.com/knightfall/envoy-dotnet-control-plane/issues/31). The earlier runtime/package pass remains distinct from later documentation-head verification. T26/GV is reported separately.

## Publication evidence

The owner approved production publication. The [original production run](https://github.com/knightfall/envoy-dotnet-control-plane/actions/runs/42336411459) attested and pushed the packages and symbols from source commit `000438a9c4d0bc0b58a5ec7bc55af10f5f9c0ccd`. After correcting external-consumer verification, the [recovery run](https://github.com/knightfall/envoy-dotnet-control-plane/actions/runs/42340393784) verified consumption from both registries and announced the existing release. The later verification workflow did not rebuild or republish the candidate. See [issue #74](https://github.com/knightfall/envoy-dotnet-control-plane/issues/74) for the full evidence and [publishing process](PUBLISHING.md) for the trust boundaries.
