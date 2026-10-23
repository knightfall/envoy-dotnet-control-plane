# .NET parity plan for go-control-plane

Reviewed on **24 October 2026**. Status: revised proposal; implementation and protocol generation have not started.

## Recommendation

Build a standalone .NET 10 library with behavioral parity for the maintained v3 functionality of the **root Go library**. Deliver a smaller working preview first, then complete the cache, server, fetch, and client-helper surface for 1.0. Prefer Protovalidate for optional runtime validation, subject to metadata and .NET runtime proofs. Keep matcher, broad catalogs, and PGV method-shaped API equivalence separately scoped.

The release claim should be “v3 server/cache/client behavior at the pinned Go baseline.” Full repository parity would additionally require the matcher, broader generated API catalogs, and generated validation behavior. Those are distinct acceptance criteria.

The caller supplies Envoy configuration. The library supplies in-memory caches, discovery protocol handling, and hosting integration. Resource translation, durable storage, and deployment orchestration remain application concerns, matching the upstream scope.

The [detailed architecture](docs/architecture/ARCHITECTURE.md), [decision register](docs/architecture/DECISIONS.md), [implementation tasks and subtasks](docs/plan/IMPLEMENTATION-PLAN.md), and [verification traceability](docs/plan/TRACEABILITY.md) expand this proposal into an actionable design. All implementation tasks remain planned.

## Baseline and repository layout

- Go reference: ../go-control-plane/ — clean main tracking origin/main.
- .NET planning repository: this directory, ../dotnet-control-plane/ — local Git repository, with no remote or commits.
- Go baseline: **67c5345367582741334e34ffc66a561b0ce32946**, committed 23 October 2026 UTC.
- Mirrored Envoy source: **82a4424f760b6ba3ffc9f2911389d061656f35c8**, from envoy/COMMIT.
- Previous Go baseline: 0b31c014c0e3d2dd3e5e8b7dddfcb638e191c4a7.
- The checkout advanced 25 commits. Changes cover generated API outputs, dependencies, and CI. There are no source changes under pkg/, docs/, README.md, ratelimit/config/, ratelimit/service/, or xdsmatcher/pkg/ across these two pins.
- Machine-readable provenance is in [upstream-baseline.json](upstream-baseline.json). The Go commit and the Envoy schema commit are separate pins; module version strings are not substitutes for them.

The [October 12 draft](docs/plan/archive/PARITY-PLAN-2026-10-12.md) is retained for comparison. The [review](docs/plan/REVIEW-2026-10-24.md) explains the revisions.

## Release scope

| Release | Deliverable | Claim |
|---|---|---|
| Preview | Reproducible protocol bindings; SnapshotCache; eight standard SotW per-type services and ADS; minimal ASP.NET Core host; a real Envoy update example | Explicit subset of v3 functionality |
| 1.0 | Delta per-type services and Delta ADS; LinearCache and MuxCache; TTL/heartbeat; status and options; unary gRPC fetches and HTTP gateway; rate-limit configuration discovery methods; SotW ADS client helper; resource/conversion helpers | Maintained root-library v3 behavior listed in the parity matrix |
| Optional additions | Protovalidate validation adapter after conformance proof; standalone xDS matcher; contrib/legacy API catalogs; PGV method-shaped API equivalents only if requested | Separate, named extensions to the compatibility claim |

See [the parity matrix](docs/plan/PARITY-MATRIX.md) for precise service coverage and upstream limitations. A generated service descriptor alone does not mean the Go library implements that service.

HTTP/REST belongs in the recommended 1.0 scope. The upstream documentation's “soon deprecated” note has not removed its implementation. The gateway and common fetch handler total 163 Go source lines including comments; supporting this adapter is a better parity tradeoff than dropping it preemptively. Unary gRPC Fetch methods are separately implemented and must not disappear with an HTTP scope choice.

Rate-limit configuration discovery is included: the Go server implements StreamRlsConfigs and FetchRlsConfigs. Its generated service declares no dedicated Delta method, and the HTTP gateway has no rate-limit route. This concerns configuration delivery; the reference module supplies no rate-limit decision engine.

## .NET package design

| Package | Responsibility |
|---|---|
| Envoy.ControlPlane.Protocol | Generated messages, descriptors, and gRPC contracts from pinned schemas |
| Envoy.ControlPlane | Snapshots, cache/watch behavior, protocol engines, resource/conversion helpers, and the SotW ADS client wrapper |
| Envoy.ControlPlane.AspNetCore | Concrete gRPC service adapters, HTTP fetch endpoints, and DI/endpoint registration |
| Envoy.ControlPlane.Validation.Protovalidate — optional | Selected runtime adapter and explicit validated-publication decorators |

Package names are provisional. Core depends on the protocol package but does not require an ASP.NET Core host. The host package depends on both. The ADS helper accepts an application-owned gRPC call invoker/channel abstraction. Use Task/ValueTask and CancellationToken for I/O; use ILogger for logging instead of reproducing Go's logging facade.

Recommend .NET 10 for runtime packages. Microsoft currently lists support through 14 November 2028; .NET 8 and 9 reach end of support on 10 November 2026. Older targets need a concrete consumer requirement. The protocol package can target a broader framework if the generation proof demonstrates this without added runtime maintenance. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)

## Protocol bindings: coverage and provider

A dependency closure starting only from DiscoveryRequest/Response is insufficient for a usable typed control plane. For example, HttpConnectionManager, the router filter, TLS, and other extension configurations appear inside Any; their schemas are not all statically imported by the discovery envelope.

Recommend a **pinned standard Envoy API catalog with usable extension types**, starting with all nonlegacy standard API inputs that the generation pipeline can compile, plus the rate-limit configuration schemas required by the Go server. Record every selected input and exclusion in a generation manifest. Include transitive dependencies regardless of their directory version; active extension schemas can use v3alpha or other version names. Treat contrib and complete legacy catalogs separately.

Applications must also be able to supply custom IMessage resources/Any payloads and register descriptors for JSON handling. Concrete types must come from one chosen binding provider: two assemblies defining the same Envoy messages create type-identity and descriptor conflicts.

Evaluate providers in this order before freezing public APIs:

1. Check whether an existing generated SDK can be pinned to the required schema content and restored reproducibly. Buf now documents C# SDKs through its NuGet feed, so owning generation is not the only possible route. The exact Envoy SDK/version and its coverage have **not** been restored or verified in this review. [Buf NuGet documentation](https://buf.build/docs/bsr/generated-sdks/nuget/)
2. Default to repository-owned generation and an ordinary packaged Protocol artifact if that exact compatibility cannot be established. Pin proto inputs, imports, compiler, and plugins; keep generation in versioned scripts. Ordinary consumers install compiled packages.
3. Treat the older Envoy.Grpc package and C# control-plane implementations as references until their current schema and behavior are established. Envoy.Grpc 1.0.1 is listed as updated in September 2020; that is insufficient evidence for this 2026 pin. [Package listing](https://www.nuget.org/packages/Envoy.Grpc), [existing C# project](https://github.com/ahmelsayed/envoy-csharp-control-plane)

At the new pin, envoy/ and contrib/ contain 1,488 generated *.pb.go files and 717 *.pb.validate.go files; ratelimit/ adds two *.pb.go files. These totals include Go-specific generated variants. They describe generation and dependency maintenance, not equivalent handwritten C# work.

The Go checkout has only a matcher test .proto. Envoy API inputs must be obtained at the Envoy source pin. The ratelimit/*.pb.go files identify additional proto paths, but this checkout does not record their original schema revision; resolve that provenance and compare descriptors during the generation checkpoint. Pin external imported schema sources as well.

## Validation and compatibility boundaries

C# protobuf/gRPC generation does not automatically recreate Go's Validate/ValidateAll APIs. Protovalidate is the successor direction, with community .NET runtimes available. Prefer an optional provider-neutral validation adapter; the [validation design](docs/architecture/VALIDATION.md) covers candidate selection, Envoy PGV annotation migration, single descriptor ownership, and conformance evidence. Adding a runtime alone does not execute existing PGV annotations. Raw SetSnapshot remains unvalidated by default, preserving source behavior. [Protovalidate](https://github.com/bufbuild/protovalidate)

Preserve these runtime behaviors:

- Custom node hashing, custom snapshot/watch implementations, status inspection, watch cancellation, and partial snapshots.
- Optional Snapshot.Consistent checks matching the actual source. SetSnapshot does not automatically call Consistent; avoid silently adding stronger validation. The implementation checks endpoint and route resource reference sets, rather than validating every possible relationship.
- Full-state SotW responses where required for clusters/listeners; named subscriptions, wildcard transitions, prefix subscriptions, and the legacy-wildcard configuration options.
- ADS readiness and optional ordering with the upstream defaults. ADS coordination is not a transaction that atomically applies all resource types.
- ACK/NACK association, stale nonces, superseded watches, first-request node identity, Delta initial resource versions, removals, unsubscribe semantics, and reconnect behavior.
- In-path callbacks: preserve ordering, error propagation, and close notifications. Await .NET callbacks in the corresponding path; do not impose a new global order across independent streams.
- Serialize sends within each gRPC stream. Design watch delivery/backpressure and cancellation together so an awaited send cannot strand global cache locks; bounded queues must not silently discard protocol responses.
- SotW/Delta TTL wrappers and the source's automatic **SotW** heartbeat loop, with a controllable TimeProvider; stream/cache cleanup and shutdown cancellation. An automatic Delta heartbeat scheduler would be a separately documented extension.
- Mode-specific nonce handling: the reviewed Delta loop does not copy SotW's stale-nonce branch or maintain a separate ACK-committed resource ledger.
- LinearCache's unsupported fetch behavior and MuxCache's missing-classification errors.
- Protobuf JSON semantics, Any descriptor registration, unknown-field preservation on binary paths, and the existing HTTP status mapping including unchanged-result 304.

## Verification and release gates

Use both the pinned Go implementation and the xDS specification. Record any disagreement instead of copying an apparent defect silently.

Compare parsed messages and protocol state transitions. Protobuf wire bytes and deterministic serialization are not universally canonical across languages or versions. Go's default resource version is SHA256 over deterministic protobuf bytes; a literal hash match in C# needs separate proof. Preserve version equality/change relationships and the acknowledgement history. Normalize only opaque generated values with a consistent mapping; do not normalize away stale-version or stale-nonce errors. [Protobuf serialization guidance](https://protobuf.dev/programming-guides/serialization-not-canonical/)

Release gates:

1. Generate or restore the chosen bindings; compile them, pack the protocol package, and consume it from a clean project. Verify Any round-trips for a realistic listener/HCM/router/TLS graph and rate-limit configuration.
2. Port representative Go behavior cases and run differential scenarios for all in-scope matrix rows, including defaults and unsupported operations.
3. Exercise ASP.NET Core gRPC loopback behavior, cancellation, callback rejection, concurrent snapshot updates, and watch cleanup.
4. Run an actual Envoy proxy: bootstrap, initial config, live endpoint/route changes, deliberate NACK, reconnect/resubscribe, SotW and Delta, and TTL expiry. Select and pin released Envoy image digests for the supported-version matrix; the latest schema pin alone is not proof of compatibility with a released Envoy binary.
5. Build and pack on CI; verify regeneration and package consumption, then document supported schema/Go/Envoy versions and every intentional .NET API departure.

No Go or .NET runtime tests, SDK restore, protocol generation, package build, or live Envoy interop were run during this proposal review. Git synchronization, source inspection, documentation checks, and primary-source research establish the planning evidence only.

## Work sequence and estimate

The earlier 12–23 and 20–36 engineer-week ranges were not calibrated by a generation proof or a complete feature inventory. Replace them with this provisional decomposition for the recommended 1.0 scope:

| Work package | Engineer-days |
|---|---:|
| Binding-provider/generation proof, source pins, clean package consumption | 2–4 |
| Snapshot/watch primitives, SotW and ADS, minimal host | 5–8 |
| Delta, Linear/Mux, TTL and configuration/default parity | 6–10 |
| Unary/HTTP fetch, ADS client, resource/conversion helpers | 3–5 |
| Differential/Envoy interop, packaging, docs, CI and release fixes | 5–8 |
| Total before contingency | 21–35 |

A useful preview is roughly **2–3 engineer-weeks**; root-library v3 parity is roughly **6–10 engineer-weeks including contingency**. These are planning judgments for one experienced engineer, not observed implementation throughput or a delivery commitment. A bounded Protovalidate/provider assessment is part of the first checkpoint; full adapter/conformance work, PGV method-shaped equivalents, complete legacy/contrib catalogs, and matcher parity are outside these estimates. Re-estimate immediately after the first checkpoint; a failed binding or metadata proof changes architecture and schedule. The [detailed backlog](docs/plan/IMPLEMENTATION-PLAN.md) decomposes the same work without adding a second estimate on top.

Counted implementation evidence: cache code is 3,215 Go source lines and server code is 1,783, including comments; their tests total 5,197 lines. The ADS client helper adds 167 source lines. Concurrency, schema resolution, and interoperability drive the risk; generated-file volume does not justify a calendar estimate on its own.

## Decisions still worth user input

The current recommended defaults are .NET 10, the three-package shape, a streaming preview followed by root-library v3 parity in 1.0, and a standard protocol catalog with extension types. These are proposals, not previously approved user choices.

Material questions are whether a named consumer needs the standalone matcher/contrib catalog in 1.0, whether older .NET targets are required, and whether “parity” must include generated validation/legacy APIs. Repository branding and remote visibility can be decided before publication; they do not block the technical review.
