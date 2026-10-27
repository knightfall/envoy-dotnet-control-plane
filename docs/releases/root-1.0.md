# Root 1.0 release preparation

T25 prepares a concrete root candidate and its review evidence. Publication needs separate explicit authorization and an authorized repository/package workflow. This change does not create a NuGet release, a tag or a GitHub release. The optional evaluator has its own T26/GV gate and is excluded from the root candidate.

## Candidate identity and version

The owner selected the `Knightfall.Envoy.ControlPlane` NuGet family. Assembly names, C# namespaces and protobuf identities retain their existing names. All packages target net10.0.

| Package ID | Version | Release scope |
|---|---|---|
| Knightfall.Envoy.ControlPlane.Protocol | 1.0.0-rc.1 | Root candidate |
| Knightfall.Envoy.ControlPlane.Validation.Annotations | 1.0.0-rc.1 | Root candidate, annotations only |
| Knightfall.Envoy.ControlPlane | 1.0.0-rc.1 | Root candidate |
| Knightfall.Envoy.ControlPlane.AspNetCore | 1.0.0-rc.1 | Root candidate |
| Knightfall.Envoy.ControlPlane.Validation | 0.1.0-t03 | Experimental research; excluded from root candidate |

The distinct prefix avoids reusing the existing public `Envoy.ControlPlane` ID. Choosing a name does not reserve it or prove publishing rights. Confirm final repository branding/visibility and publishing rights through the separate publication checklist below. Package-content and executable consumer checks verify new IDs/versions with unchanged assembly names and dependency ownership.

## Candidate release notes

The root candidate adds the complete selected v3 server/cache/helper surface over the historical SotW preview: Delta subscriptions/cache/streams and eleven adapters; LinearCache and MuxCache; TTL response envelopes and explicit SotW heartbeat lifecycle; all nine Fetch methods and eight optional HTTP routes; rate-limit configuration SotW; the focused SotW ADS client; complete selected resource/reference/Struct/conventional-name helpers. All thirty selected gRPC methods have generated-client HTTP/2 proof.

The implementation uses idiomatic C# channel delivery, asynchronous publication/disposal, cancellation tokens, `TimeProvider` and ordinary ASP.NET Core DI/authorization/logging. Original PGV metadata and authoritative generated message ownership remain intact. Core and hosting contain no evaluator/CEL dependency. See [usage/migration](../usage.md), [public API contracts](../public-api.md) and [compatibility](../compatibility/root-1.0.md) before upgrading.

Known limits include stable caller-owned messages, source SnapshotCache prefix/readiness behavior, raw Delta TTL omission, unsupported Linear Fetch, application-owned reconnect/policy and explicit opaque-Any registration. The default Envoy 1.40.1 SotW CDS mux removes clusters on payload-free heartbeats; the separately proven explicit Delta TTL path does not add an automatic scheduler. The proxy matrix covers one released version on Linux amd64/arm64 and a four-family graph. The complete selected catalog is broader than that live graph. Broader contrib/legacy catalogs and standalone matcher remain deferred. Full optional-validator conformance is not included.

## Engineering checklist

- [x] Reconcile the implemented service/cache/helper matrix, necessary C# adaptations and optional validation boundary in the root compatibility report.
- [x] Record public API defaults, ownership, cancellation, error and migration contracts with links to their implementation/evidence.
- [x] Select the owner-approved NuGet prefix and set four coherent root candidate package versions without changing assembly/protobuf identities. Confirm actual publishing rights only before authorized publication.
- [x] Locally build/pack the concrete candidate, inspect IDs/versions/retained assemblies/symbols/docs/notices/dependency ownership, and run fresh package-consumer suites and both executable graphs. Exact committed-head CI remains below.
- [x] Locally verify unchanged binding/descriptor fingerprints and run required source/native/lifecycle/update checks against the candidate working tree; retain its dirty-tree status accurately.
- [x] Record Linux/macOS/Windows and Linux amd64/arm64 proxy results for runtime/package commit `eebfe5783cae67c840f29c86a1a347f83962402e`; link immutable workflow runs and retain scope limits in the compatibility report.

Final signoff is recorded in [PR #72](https://github.com/knightfall/envoy-dotnet-control-plane/pull/72) and [issue #31](https://github.com/knightfall/envoy-dotnet-control-plane/issues/31): address automatic Codex findings, verify all CI/review gates on the final head and record its immutable merge result before closing T25. The earlier runtime/package pass does not waive verification of a later documentation head. Keep T26/GV separate.

## Publication checklist — separate authorization required

- [ ] Confirm final repository branding, remote visibility and package publishing rights for every ID.
- [ ] Explicitly authorize the exact packages/version and publication workflow, using the verified immutable candidate and reviewed release notes.
- [ ] Publish only through that authorized workflow and verify actual package availability and clean external consumption.

An unchecked publication item does not authorize a workaround or imply a package was published. T25's engineering preparation may complete without publication; it must leave the distinction explicit.
