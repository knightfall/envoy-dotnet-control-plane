# .NET parity plan for go-control-plane

**Status:** Draft; the scope questions at the end need answers before implementation starts.

## Workspace layout and baseline

This workspace keeps the source and proposed .NET library in separate folders:

```text
csharp-control-plane/
├── go-control-plane/       # upstream Go reference clone
└── dotnet-control-plane/   # local .NET library planning repository
```

The upstream clone is on `main` at `0b31c014c0e3d2dd3e5e8b7dddfcb638e191c4a7` (2026-10-11), mirrored from Envoy commit `9a24324c9a64bf890f97595dfd9e319b283a1647`. It is clean and tracks `origin/main`. The `dotnet-control-plane` folder is a local Git repository with no remote or implementation yet.

## Product boundary

The reference is reusable xDS infrastructure: a generic API server plus in-memory configuration caches. The caller provides and updates Envoy resources. It does not translate application or infrastructure models into Envoy configuration, and it is not a complete fleet control plane. The .NET library should keep that same boundary.

## Recommended first release

Target Envoy xDS v3 and ship a hostable gRPC server with State-of-the-World (SotW), ADS multiplexing, and Delta/Incremental streams, backed by the reference cache behaviors:

- Per-node versioned snapshots, resource name/type handling, node hashing, status, and consistency checks.
- ADS watches that wait for the referenced resource set before replying, so related resource collections can update atomically.
- A single-type-URL linear cache with resource versions and name, prefix, and wildcard subscriptions.
- Cache multiplexing across type URLs, resource removals, TTLs, and heartbeats.
- Synchronous lifecycle/request/response callbacks with equivalent ordering and cancellation behavior.
- The v3 rate-limit **configuration discovery** RPC exposed by the upstream server, with its generated protocol dependencies. This is not a rate-limit decision engine.

Ship generated Envoy protocol types as a separate package, initially covering the v3 protocol/resource dependency closure needed by the server and cache. Generate C# from pinned upstream `.proto` sources in the [Envoy data-plane API](https://github.com/envoyproxy/data-plane-api); do not translate Go-generated `.pb.go`, validation, or vtprotobuf files. Google documents the C# `protoc` generator, and Microsoft documents `.proto`-driven message and gRPC service generation for .NET ([C# generated code](https://protobuf.dev/reference/csharp/csharp-generated/), [gRPC on .NET](https://learn.microsoft.com/aspnet/core/grpc/)).

Defer HTTP/REST and the standalone xDS matcher module until scope is confirmed. The reference docs describe REST as soon to be deprecated, while the README says v2 control-plane code has been removed. The rate-limit configuration discovery service is part of the main v3 server interface, so its generated service and messages belong in the v3 server surface; the upstream does not provide a rate-limit decision engine. The generated Envoy module still contains API versions beyond the active v3 server, so “full protocol parity” and “server parity” are different scopes.

## Reference surface and proposed scope

| Upstream area | Current behavior in the clone | Proposed .NET scope |
|---|---|---|
| `pkg/server/v3` and transport handlers | Registers v3 xDS service handlers; supports SotW, Delta, REST, and callbacks | v3 gRPC SotW/ADS and Delta in core; REST behind a later decision |
| `pkg/cache/v3` snapshot/simple cache | Versioned snapshots per node, watchers, ADS gating, status and optional TTL heartbeats | v1 core |
| `pkg/cache/v3` linear cache | Single-type resource collection, per-resource versions and subscriptions | v1 core after snapshot semantics |
| `pkg/cache/v3` mux | Selects a cache based on request classification | v1 core after the individual caches |
| `envoy/` and `contrib/` generated modules | Large, continuously mirrored Envoy and extension protobuf surface | Separate protocol package; initially only the v3 dependency closure; broaden only if chosen |
| `ratelimit/` | Generated rate-limit configuration and discovery service protobufs; the discovery service is included by `pkg/server/v3.Server` | Include the service and messages in v1 server/protocol parity; no decision engine is in the upstream module |
| `xdsmatcher/` | Matcher tree creation/evaluation, legacy conversion, and extension registry | Optional package/phase; inventory behavioral coverage before promising full parity |
| REST/JSON | HTTP/1.1 fetch path in the reference | Deferred by default; include if required for compatibility |
| Resource translation | Explicitly outside the upstream library’s scope | Out of scope |

One important scope detail: the reference repository has 1,476 generated `*.pb.go` files and 711 generated validation files, but only one `.proto` file in the checkout (`xdsmatcher/test/proto/test.proto`). `envoy/COMMIT` pins the mirrored Envoy source revision; the sync workflow generates from a separate Envoy checkout. A maintainable .NET protocol package therefore needs its own pinned proto-source and generation pipeline.

## Proposed .NET project shape

Names are provisional pending confirmation.

```text
src/
  Envoy.ControlPlane.Protocol/       # generated v3 protobuf messages and gRPC bases
  Envoy.ControlPlane/                # cache abstractions, snapshots, watches, server behavior
  Envoy.ControlPlane.AspNetCore/     # optional registration helpers, if useful to consumers
tests/
  Envoy.ControlPlane.Tests/
  Envoy.ControlPlane.Protocol.Tests/
examples/
  MinimalControlPlane/
```

Keep the cache and protocol behavior reusable from an application; isolate ASP.NET Core registration conveniences from core abstractions. Use standard .NET gRPC/Protobuf generation rather than hand-maintained message classes. Pin the Envoy proto source revision and generated package versions in CI so an upstream API sync is deliberate and reviewable.

## Implementation sequence

1. **Close scope and prove code generation (1–2 weeks).** Inventory exported v3 methods/types, decide legacy and auxiliary coverage, check for existing C# Envoy bindings, and generate a small end-to-end proto/gRPC slice from the pinned source commit. Record the dependency closure and namespace strategy.
2. **Protocol package and repository baseline (2–4 weeks).** Set up reproducible code generation, version pinning, package metadata, CI, and cross-language wire-format fixtures for the selected protocol surface.
3. **Snapshot cache (2–4 weeks).** Port snapshot/resource abstractions, node-keyed versions, consistency checks, subscriptions/watch lifecycle, status, ADS gating, and SOTW response construction.
4. **SotW gRPC and ADS server (2–4 weeks).** Implement resource-specific and aggregated services, callbacks, cancellation, ACK/NACK handling, and stream lifecycle compatibility.
5. **Delta and remaining cache behavior (3–5 weeks).** Add subscribe/unsubscribe and per-resource version behavior, removals, `LinearCache`, `MuxCache`, TTL, and heartbeats. Add concurrency and reconnect coverage.
6. **Compatibility and release work (2–4 weeks).** Port examples, build an Envoy interop path, add the chosen REST/auxiliary work, publish packages, and document migration from Go API concepts to idiomatic C#.

These are preliminary single-engineer estimates. A v3 server/cache library with generated protocol dependencies is roughly **12–23 engineer-weeks**. Broader parity including the full generated API set, REST, and the standalone matcher module is roughly **20–36 engineer-weeks**. Supporting v2 would add more API-generation and compatibility work even though the current upstream server has removed v2. Re-estimate after the code-generation spike and the exported-API inventory.

## Compatibility and verification strategy

No tests were run or added during this planning pass. Before release, use the pinned Go clone as a behavioral oracle:

- Port representative upstream tests for snapshot versions, watch readiness, ADS resource dependencies, stale responses, Delta subscriptions/removals, TTL refreshes, and cancellation.
- Run differential cases against the Go and .NET implementations using identical discovery requests and compare response fields, versions, removed-resource sets, callback events, and gRPC status codes.
- Add cross-language Protobuf wire fixtures for `DiscoveryRequest`, `DiscoveryResponse`, and `DeltaDiscovery*` including `Any` payloads and TTL wrappers.
- Exercise loopback gRPC streams for first response, ACK/NACK, updates, reconnect/resubscribe, ADS, and Delta. Add Envoy integration only after the library-level contract is stable.
- Test concurrent snapshot updates and watch creation/cancellation deterministically; preserve the upstream’s synchronous callback contract where observable.
- In CI, build generated sources from the pinned proto commit and fail on unreviewed generated diffs. Keep API sync separate from ordinary library changes.

## Source map and scope divergences

The seven files in [`docs/codebase/`](docs/codebase/) map the upstream structure and evidence used for this draft.

- **Requested outcome:** a .NET parity library. **Reference reality:** the main README explicitly limits the project to generic server/cache infrastructure and excludes resource translation.
- **“Parity” ambiguity:** the active control-plane implementation is v3, but the mirrored generated API module includes old and current API packages. Full generated-proto parity is a much broader commitment than matching the current v3 server/cache.
- **Auxiliary modules:** rate-limit configuration discovery is included by the main v3 server interface and belongs in server parity; it is distinct from a rate-limit decision engine. The separate xDS matcher implementation is optional to a server/cache release.
- **Version-source gap:** generated Go bindings are checked in, but their `.proto` inputs are not (apart from the matcher test proto). The .NET repo needs a deliberate source pin and generation workflow.

## Questions to lock before implementation

1. **First-release scope:** proceed with v3 gRPC server/cache plus the rate-limit configuration discovery RPC (SotW + ADS + Delta, Snapshot/Linear/Mux caches), or also include the standalone xDS matcher module?
2. **Protocol package breadth:** keep generated protocol types as a separately versioned package and generate the v3 dependency closure for the selected server APIs, or include all Envoy/contrib APIs and legacy versions too?
3. **REST:** keep REST/JSON out of v1 as proposed, or require it in the first release?
4. **Project defaults:** is `dotnet-control-plane` an acceptable local/repository name, and should the library target .NET 10 like the scaleset project, or support an older target as well?
5. **Hosting boundary:** keep cache/protocol behavior framework-neutral and offer ASP.NET Core registration helpers as a separate package, or make ASP.NET Core a core dependency?
