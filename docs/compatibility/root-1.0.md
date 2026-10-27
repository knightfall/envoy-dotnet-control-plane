# Root 1.0 compatibility report

**Status: root candidate preparation, 27 October 2026.** This report reconciles the implemented root surface at merged commit `4f58a9d576979564afa9b435839c9d21cb5e368a`. The T25 execution section below records the immutable runtime/package candidate evidence. Package identity is resolved as `Knightfall.Envoy.ControlPlane.*`, preserving CLR/protobuf names. The [release checklist](../releases/root-1.0.md) and implementing PR separately record final-head CI/review gates. **Validation not included:** full optional-validator T26/GV acceptance is incomplete.

The scope is the selected v3 root library, not every service or submodule in the source repository. The [parity matrix](../plan/PARITY-MATRIX.md) and [traceability catalog](../plan/TRACEABILITY.md) define the requirements. The earlier [SotW preview manifest](../preview/compatibility.json) is historical evidence for a smaller surface.

## Source, generation and runtime axes

The immutable [source lock](../../schemas/upstream.lock.json) records canonical remote repositories, archive checksums, notices and excluded build-only patches. Its source commits are:

| Input | Commit |
|---|---|
| Go behavior reference | `67c5345367582741334e34ffc66a561b0ce32946` |
| Envoy schemas | `82a4424f760b6ba3ffc9f2911389d061656f35c8` |
| Rate-limit schemas | `0d9cfd0b3934c6c66bd97544592f3f539e7138cc` |
| CNCF xDS schemas | `ee656c7534f5d7dc23d44dd611689568f72017a6` |
| PGV annotation schemas | `92b9a7df69ca9f71bfc492f7a90adf4d36eab569` |
| Protobuf schemas | `35cd01f9fe9afbeea38cc7b979a3b6bfcde82c03` |
| Google APIs | `2193a2bfcecb92b92aad7a4d81baa428cafd7dfd` |
| OpenTelemetry schemas | `c0a98a1847d3124ac5f9ecd02d0e2d2732bbb590` |
| Prometheus model | `eb136e513d419e0c31ad750922f0a6f7675c2dee` |
| CEL schemas | `7f3c4c513b42d471d0be9439bf5bde38f45f8404` |

Bindings are owned generation through Buf **1.75.0**, official `protocolbuffers/csharp:v35.1` revision 1 and `grpc/csharp:v1.84.0` revision 1. Managed mode is disabled. The SDK is **10.0.400**, all library targets are **net10.0**, comparison Go is **1.26.4**, Google.Protobuf is **3.35.1** and gRPC packages are **2.84.0**. [CPM](../../Directory.Packages.props), project lock files and source/tool locks govern the actual graph. Roslynator 5.0.1 checks authored code with warnings as errors.

| Fingerprint | SHA-256 |
|---|---|
| Selected catalog | `6af96cd168d1808563d66bcb316fadf853a22e55647616aa37c972ff91afe713` |
| Original descriptor set | `c34501434b0580470167d4a257269e25d921dbd5c6ba4968255642cf1e3d10a4` |
| Original generated set, 540 C# files | `75f703a5ccf75567169bfdc5d7e4645c9648d6bb8218f7e075825be12f2c929c` |
| Derived descriptor set | `7403284aec07c54c08d450dbc5ccea77f9c713e88c971c22421c37283e59e9f9` |
| Derived generated set, 540 C# files | `3d68c6a4dd31907c29c8c8de05136ded90ada8de0bf9c4efe118429d2ebca51d` |
| Shared derived annotation source | `769ac71289df8ae63979e4713cc89923b3fd1ccc115107a670f783e89383c0f0` |

The [binding report](../../schemas/BINDINGS.md) documents the three fingerprinted generated C# naming corrections. Protobuf full names, fields, original PGV options and well-known-type ownership remain unchanged. The selected standard catalog has a compiled/packaged consumer proof; broader catalogs are not inferred from discovery-envelope dependencies or opaque `Any` references.

## Implemented transport inventory

All methods below have generated-client HTTP/2 evidence. There are **ten SotW, eleven Delta and nine Fetch methods**, across twelve mapped services. ADS dispatch remains generic by type URL; a missing dedicated method does not prevent a type from travelling through ADS.

| Service | SotW | Delta | Unary Fetch |
|---|---|---|---|
| AggregatedDiscoveryService | StreamAggregatedResources | DeltaAggregatedResources | None |
| ClusterDiscoveryService | StreamClusters | DeltaClusters | FetchClusters |
| EndpointDiscoveryService | StreamEndpoints | DeltaEndpoints | FetchEndpoints |
| ListenerDiscoveryService | StreamListeners | DeltaListeners | FetchListeners |
| RouteDiscoveryService | StreamRoutes | DeltaRoutes | FetchRoutes |
| ScopedRoutesDiscoveryService | StreamScopedRoutes | DeltaScopedRoutes | FetchScopedRoutes |
| SecretDiscoveryService | StreamSecrets | DeltaSecrets | FetchSecrets |
| RuntimeDiscoveryService | StreamRuntime | DeltaRuntime | FetchRuntime |
| ExtensionConfigDiscoveryService | StreamExtensionConfigs | DeltaExtensionConfigs | FetchExtensionConfigs |
| VirtualHostDiscoveryService | None | DeltaVirtualHosts | None |
| LocalityEndpointDiscoveryService | None | DeltaLocalityEndpoints | None |
| RateLimitConfigDiscoveryService | StreamRlsConfigs | No dedicated RPC | FetchRlsConfigs |

The first eleven services use their generated `envoy.service.*.v3` names; rate-limit uses `ratelimit.service.ratelimit.v3`. [T14 transport evidence](../plan/T14-DELTA-GRPC.md) and [T18 Fetch/gateway evidence](../plan/T18-FETCH-GATEWAY.md) identify actual call assertions. Generated contracts for unrelated Envoy services do not provide server implementations here.

The opt-in HTTP gateway maps `/v3/discovery:endpoints`, `clusters`, `listeners`, `routes`, `scoped-routes`, `secrets`, `runtime` and `extension_configs`. It overrides the body's type URL with route selection, uses protobuf JSON/explicit Any descriptors, and preserves 200/304/400/404/500 semantics. No ADS/VHDS/LEDS/rate-limit HTTP route is claimed. Host routing, UTF-8/JSON parsing, connection/body errors and authorization remain documented adaptations.

## Requirements and root feature coverage

| Requirements / matrix surface | Status and concrete evidence |
|---|---|
| R01–R02: provenance, generated provider, preserved metadata | Implemented. [T01](../../schemas/PROVENANCE.md), [T02](../../schemas/BINDINGS.md), [T03](../../schemas/VALIDATION.md), source/tool locks and fresh Protocol/annotation ownership checks. Full evaluator conformance is separate. |
| R03–R04: resources, snapshots, catalog, custom descriptors | Implemented. [T05](../plan/T05-RESOURCES-AND-SNAPSHOTS.md), original/derived binding tests and [T20 helpers](../plan/T20-HELPER-COMPATIBILITY.md). Stable caller-owned messages, partial snapshots and explicit consistency/hash construction. |
| R05–R06: SnapshotCache, custom watcher/snapshot/fetch | Implemented. [T06](../plan/T06-WATCHES.md), [T07](../plan/T07-SNAPSHOT-CACHE.md), [T16](../plan/T16-MUX-CACHE.md), [T18](../plan/T18-FETCH-GATEWAY.md). Channel-primary one-shot registration and separate polling capability. |
| R07–R11: SotW subscription, ADS/options, callbacks, status | Implemented. [T08](../plan/T08-SOTW-SUBSCRIPTIONS.md), [T09](../plan/T09-SOTW-STREAM-ENGINE.md), [T10](../plan/T10-ORDERED-ADS.md) and [T22](../plan/T22-ROOT-LIFECYCLE.md). Readiness/order independent; default unordered and legacy wildcard enabled. |
| R12–R13: Delta subscription/cache/stream/removals | Implemented. [T13](../plan/T13-DELTA-SUBSCRIPTIONS.md), [T14 cache](../plan/T14-DELTA-CACHE.md), [streams](../plan/T14-DELTA-STREAMS.md), [transport](../plan/T14-DELTA-GRPC.md) and [T23](../plan/T23-ROOT-ENVOY.md). Source bookkeeping, without a SotW nonce filter or ACK ledger. |
| R14–R15: LinearCache and MuxCache | Implemented. [T15](../plan/T15-LINEAR-CACHE.md), [T16](../plan/T16-MUX-CACHE.md), 31 and twelve actual Go cases. Linear Fetch remains unsupported; mux retains child ownership. |
| R16: TTL envelopes, SotW timer, lifecycle | Implemented with explicit limits. [T17](../plan/T17-TTL-HEARTBEATS.md), thirteen Go cases and native timer disposal. Raw Delta omits TTL like the source; custom Delta envelopes can carry it. Caller owns the SotW loop. The released default SotW CDS heartbeat limitation remains below. |
| R17–R18: Fetch/gateway/JSON | Implemented. [T18](../plan/T18-FETCH-GATEWAY.md), fourteen cache Fetch, nine shared-engine and thirty gateway Go cases plus HTTP/2 and native Any/authorization checks. |
| R19: focused SotW ADS client | Implemented. [T19](../plan/T19-SOTW-ADS-CLIENT.md), twelve actual Go traces, native lifecycle/channel-ownership and Kestrel calls. Reconnect/decoding remain application-owned. |
| R20: reference/type/name/Struct/well-known helpers, logging | Implemented. [T20](../plan/T20-HELPER-COMPATIBILITY.md), complete symbol map, seventeen conversion Go cases, compiled example graph and `ILogger` hosting. |
| R21–R22: hosting, source comparison, resilience/proxy | Implemented in the advertised matrix. [T21](../plan/T21-ROOT-DIFFERENTIAL.md), [T22](../plan/T22-ROOT-LIFECYCLE.md), [T23](../plan/T23-ROOT-ENVOY.md). Source/native/HTTP2/proxy proof remains distinct. |
| R23: bounded validator feasibility | Implemented as T03 research: 33 Go PGV cases, preserved metadata and source-built candidate. This is not GV. |
| R24: optional semantic validator | Incomplete T26/GV. Outside the root release candidate; unsupported and unevaluated rules remain exposed. |
| R25: packaging/CI/updates/docs | T24 implemented; [T25 checklist](../releases/root-1.0.md) records the selected package identity and verified candidate packages; exact final-head CI/review is recorded in the implementing PR. |
| R26: standalone matcher / broader contrib and legacy catalogs | Deferred T27/T28; explicit scope decision required. No v2 runtime or whole-repository parity claim. |

## Released Envoy matrix and operational limits

The [image lock](../../tests/resources/envoy-images.lock.json) pins Envoy **1.40.1**, source `86af01fdec40c31d025c495a4aa303c4126b2fd0`, independently of the newer schema source. T23 PR #69 verified **35 checks per architecture**: twenty SotW ordinary/ordered ADS checks, nine Delta ADS checks and six explicit Delta TTL checks.

| Linux architecture | Actual Envoy image digest |
|---|---|
| amd64 | `sha256:7b088327f38b3d5d2dac1855b2722ab398dbfe32d38929b4a9599d740d746d8b` |
| arm64 | `sha256:a62e7a0d52fb00f708bffeb9be373af0b1858bcede67575edcfe4a1bbffe5384` |

The image lock also pins architecture-specific SDK images. The live graph covers CDS/EDS/LDS/RDS traffic, endpoint/route updates, SotW deliberate NACK/repair, Delta removal/re-add, reconnect initial versions and config-dump/admin convergence. Other service families have generated-client transport and applicable source proof, without a claim that every proxy workload was run. TLS/authentication deployment, additional proxy versions, older targets and an automatic performance SLA are not established by this probe. The accepted gRPC/Kestrel baseline remains manual.

**Known released-proxy limitation:** Envoy 1.40.1's default SotW CDS mux treats the source-equivalent payload-free heartbeat as empty CDS state and removes the cluster. Do not advertise the built-in SotW scheduler as interoperable for that configuration. V33 uses a test-only caller-supplied Delta watcher through existing pass-through contracts: refresh prevents expiry, stopping refresh produces expiry/503/admin removal, and changed payload repairs it while the original cache snapshot remains retained. That proof adds no automatic Delta scheduling feature. See [T23's observations and retained reports](../plan/T23-ROOT-ENVOY.md).

## Necessary C# adaptations and retained source limits

[The API contract](../public-api.md) records ownership, cancellation and errors for every selected extension surface. Channels establish publication order; an await-one adapter serves individual readers. Asynchronous watch disposal settles owned writes before subscription changes. Cancellation tokens, standard timers/clock injection, serial asynchronous writers, ordinary DI/authorization and `ILogger` replace Go-specific mechanics while keeping tested outcomes.

Resource retention is stable-by-convention. Type versions are opaque, protobuf serialization is not universally canonical across runtimes, and Delta payload hashes need not have identical bytes in Go and .NET. TimeSpan precision is 100 ns. Application-provided descriptor registries own opaque `Any` resolution. Struct conversion returns new messages, and exception/cleanup aggregation follows documented .NET behavior. The client disposes replaced calls without taking ownership of its invoker/channel.

SnapshotCache exact-name ADS readiness, prefix selection and overlapping selection outcomes stay source-matched. Ordered ADS does not imply a transaction. The SotW timer scans SotW watches only; raw Delta TTL omission and Linear Fetch's unsupported result remain observable. No stronger quota, fairness, rollback, persistence or retry policy is introduced. The [T21 dispositions](../plan/T21-ROOT-DIFFERENTIAL.md) map these limits to source/native cases.

## Original annotations and optional evaluator boundary

The original catalog contains **523 files**, **1,244 field-rule entries**, **127 oneof-option entries**, **59 distinct rule paths** and **94 Any fields**. The derived catalog adds the annotation definition while retaining original options. [rules.summary.json](../../schemas/validation/rules.summary.json), the descriptor inventories and [validation source lock](../../schemas/validation/sources.lock.json) are authoritative.

T03 pins source-built `telus-labs/protovalidate-net` at `88e9c909e82c569bba7d6d722dd5c6b76ba9e965`, migration metadata at `04cd1e0187b7e570f3bb0f86a31daf1f2ad7237e`, and CEL 0.3.3 only in the separate evaluator. Its 33-case PGV corpus SHA-256 is `d65996c87fbedc83bad927ec17b268f6d039e6318e6c4764f5e318016e2b1f52`. [The feasibility report](../../schemas/VALIDATION.md) records the bounded source adaptations and oneof migration correction. Published ProtoValidate/Celly packages are not runtime dependencies of this source implementation; historical provider research remains retained.

These counts are inventory, not conformance coverage. Full claimed-rule/combination conformance, Any/unsupported/evaluator-fault distinctions and per-update affected-rule acceptance remain T26. Root update reports explicitly keep validation acceptance false and unevaluated constraints visible. Cache publication invokes no evaluator, and root-only consumers contain no evaluator/CEL dependencies. A green root gate cannot approve GV by omitting unknown rules.

## Verification evidence and remaining gates

The T24 baseline in PR #70 merged after all eighteen exact-head checks and automatic Codex review cleared. Its local repository verification observed **484 solution tests**, sixteen actual Go comparison modes / **260 cases**, fifteen injected semantic mismatches plus two nonce negatives, five package/symbol pairs, fresh Protocol/Validation/Core/Hosting suites of **14/18/264/109** and both executable package-graph consumers. These results belong to the earlier immutable revision and are not relabeled as T25 execution. T22 additionally records 192 repeated transport streams / 768 responses with explicit cleanup assertions. T23's two architecture runs are recorded separately above.

For the candidate, regenerate and verify from the repository root:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
dotnet run --project tools/Envoy.ControlPlane.Tools -- envoy verify
```

Retain the generated fingerprints, packages, consumer locks and reports. Linux/macOS/Windows CI establishes its own native/package results; the proxy workflow establishes Linux amd64/arm64 results. Exact-head automatic Codex PR review is the review gate. The [release checklist](../releases/root-1.0.md) must record the candidate's observed results and outstanding items before claiming G5 or closing T25. Source provenance/binding proof remains G0/G1, implemented root coverage G3, tested interop/lifecycle G4, final package/docs/API readiness G5, and optional conformance GV. Candidate preparation alone authorizes neither NuGet publication nor a GitHub release.

## T25 candidate execution

Local macOS arm64 verification of the candidate working tree passed after the owner selected `Knightfall.Envoy.ControlPlane.*`. The four root packages use `1.0.0-rc.1`; the separate experimental evaluator uses `0.1.0-t03`. Package contents verify exact NuGet IDs/versions while retaining the original Envoy.ControlPlane assembly filenames, XML documentation, symbols and notices. Clean executable consumers load those original assembly identities from the newly named packages and enforce exact dependency/shared-framework ownership. External locked dependencies are unchanged.

`repository verify` passed **484 solution tests / 0 failed / 0 skipped**, all sixteen Go modes / 260 root cases, fifteen semantic corruptions plus both nonce negatives, five package/symbol pairs, fresh suites **14/18/264/109**, both executable graphs and original/derived fingerprint/reproducibility checks. Compiler and Roslynator reported zero warnings/errors; packing retained the three existing notices for non-packable executable projects. The bounded 33 PGV cases also matched, without establishing GV. Reports are under Protocol's `obj/upstream/results/`.

This local run tested uncommitted candidate changes; its reproducibility snapshot deliberately reports the prior commit and `workingTreeDirty: true`. It is supporting evidence, not an exact-commit release pass. [PR #72](https://github.com/knightfall/envoy-dotnet-control-plane/pull/72) must establish the committed candidate's platform/proxy checks and automatic Codex review before merge. No NuGet package, tag or GitHub release has been published.

### Committed candidate platform evidence

Runtime/package commit **`eebfe5783cae67c840f29c86a1a347f83962402e`** passed [Upstream verification run 40457401805](https://github.com/knightfall/envoy-dotnet-control-plane/actions/runs/40457401805) on Linux, macOS and Windows. Each complete verification ran the current native suites, sixteen root Go modes / 260 cases and retained negative checks, packed all five package/symbol pairs, ran fresh **14/18/264/109** test consumers and both executable graphs, and emitted reproducibility reports. Linux uses the maintained per-project coverage path; macOS/Windows report the combined 484-test native run. No failure/skip is substituted for an executed pass.

The same commit passed [released-Envoy run 40457401905](https://github.com/knightfall/envoy-dotnet-control-plane/actions/runs/40457401905): **35 V31–V33 checks on each of Linux amd64 and arm64**, using the image digests recorded above. [CodeQL run 40457395364](https://github.com/knightfall/envoy-dotnet-control-plane/actions/runs/40457395364) also passed. These are observed results for the named commit, rather than a claim that a subsequent documentation commit has already run. PR #72 records the final head's CI/automatic review and immutable merge result; merge requires both to clear on that exact head. Publication remains separately unauthorized.
