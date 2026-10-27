# Root 1.0 release preparation

T25 prepares a concrete root candidate and its review evidence. Publication needs separate explicit authorization and an authorized repository/package workflow. This change does not create a NuGet release, a tag or a GitHub release. The optional evaluator has its own T26/GV gate and is excluded from the root candidate.

## Candidate identity and version

The intended four candidate assemblies are Protocol, Validation.Annotations, Core and ASP.NET Core, all targeting net10.0 at proposed package version `1.0.0-rc.1`. Their current development IDs/versions remain in project metadata until the owner chooses the final package identity. Public NuGet already contains `Envoy.ControlPlane`; package-ID ownership must be resolved rather than inferred from a repository name or an absent search result. Changing NuGet IDs must preserve existing C# namespaces, assembly names and protobuf identities. The source-built optional evaluator retains its experimental version and is not promoted with the root graph.

## Candidate release notes

The root candidate adds the complete selected v3 server/cache/helper surface over the historical SotW preview: Delta subscriptions/cache/streams and eleven adapters; LinearCache and MuxCache; TTL response envelopes and explicit SotW heartbeat lifecycle; all nine Fetch methods and eight optional HTTP routes; rate-limit configuration SotW; the focused SotW ADS client; complete selected resource/reference/Struct/conventional-name helpers. All thirty selected gRPC methods have generated-client HTTP/2 proof.

The implementation uses idiomatic C# channel delivery, asynchronous publication/disposal, cancellation tokens, `TimeProvider` and ordinary ASP.NET Core DI/authorization/logging. Original PGV metadata and authoritative generated message ownership remain intact. Core and hosting contain no evaluator/CEL dependency. See [usage/migration](../usage.md), [public API contracts](../public-api.md) and [compatibility](../compatibility/root-1.0.md) before upgrading.

Known limits include stable caller-owned messages, source SnapshotCache prefix/readiness behavior, raw Delta TTL omission, unsupported Linear Fetch, application-owned reconnect/policy and explicit opaque-Any registration. The default Envoy 1.40.1 SotW CDS mux removes clusters on payload-free heartbeats; the separately proven explicit Delta TTL path does not add an automatic scheduler. The proxy matrix covers one released version on Linux amd64/arm64 and a four-family graph. The complete selected catalog is broader than that live graph. Broader contrib/legacy catalogs and standalone matcher remain deferred. Full optional-validator conformance is not included.

## Engineering checklist

- [x] Reconcile the implemented service/cache/helper matrix, necessary C# adaptations and optional validation boundary in the root compatibility report.
- [x] Record public API defaults, ownership, cancellation, error and migration contracts with links to their implementation/evidence.
- [ ] Resolve NuGet identities and ownership with the repository owner; set four coherent root candidate package versions without changing assembly/protobuf identities.
- [ ] Build/pack the concrete candidate, inspect symbols/docs/notices/dependency ownership and run clean package-consumer suites and both executable graphs.
- [ ] Verify unchanged binding/descriptor fingerprints and run the required source/native/lifecycle/update checks against the candidate.
- [ ] Record current-head Linux/macOS/Windows and Linux amd64/arm64 proxy results; retain artifacts and list any unexecuted scenarios.
- [ ] Address automatic Codex PR findings, confirm all current-head CI/review gates and record the immutable verified head and merge commit.
- [ ] Close T25 only after its engineering acceptance is supported by that evidence. Keep T26/GV separate.

## Publication checklist — separate authorization required

- [ ] Confirm final repository branding, remote visibility and package publishing rights for every ID.
- [ ] Explicitly authorize the exact packages/version and publication workflow, using the verified immutable candidate and reviewed release notes.
- [ ] Publish only through that authorized workflow and verify actual package availability and clean external consumption.

An unchecked publication item does not authorize a workaround or imply a package was published. T25's engineering preparation may complete without publication; it must leave the distinction explicit.
