# .NET Envoy control plane

A .NET library for building xDS management servers, with behavioral parity for the selected v3 root-library surface of Envoy's Go control plane. Applications supply configuration and own rollout policy, persistence and authentication. The library supplies caches, subscriptions, protocol engines, generated messages and ASP.NET Core transports.

The selected root implementation and T25 candidate preparation are implemented in [PR #72](https://github.com/knightfall/envoy-dotnet-control-plane/pull/72), which records exact-head CI and automatic review. **T26 optional validation has a separate [conformance report](docs/plan/T26-VALIDATION-CONFORMANCE.md), with explicit scope and exceptions. Candidates remain unpublished; release publishing is a separate workflow.** Read the [root compatibility report](docs/compatibility/root-1.0.md) for the exact implemented scope, source pins, observed evidence and remaining release gates. The [SotW preview report](docs/preview/README.md) records the earlier, narrower milestone.

## What is implemented

- SnapshotCache, LinearCache and MuxCache; stable resource snapshots, explicit reference consistency, status and one-shot channel-based watches.
- SotW and Delta, separate services and ADS, optional ordered SotW ADS, callbacks, cancellation and host cleanup.
- All **30 selected gRPC methods**: ten SotW, eleven Delta and nine unary Fetch methods. The opt-in HTTP gateway supplies the eight upstream discovery routes.
- Resource names/type URLs, reference extraction, message/Struct conversion, conventional extension names and a focused SotW ADS client.
- TTL response envelopes and caller-owned SotW heartbeat scheduling. The [released-Envoy report](docs/plan/T23-ROOT-ENVOY.md) documents the default Envoy SotW CDS heartbeat limitation and the separately proven explicit Delta refresh/expiry path.

Generated Protocol types cover the pinned standard schema catalog, including commonly used extension messages. Broader contrib/legacy catalogs and the standalone xDS matcher remain deferred. Original PGV options are preserved; **semantic validation is explicit and optional, with pinned rule coverage and declared compatibility exceptions**. Publishing a snapshot invokes neither a validator nor a consistency check.

## Run the example

Use the SDK in [global.json](global.json). A fresh checkout must generate bindings before building the example. Choose a writable cache directory outside this checkout:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <source-cache>
dotnet run --project examples/Envoy.ControlPlane.Example
```

Generation retrieves immutable sources and pinned Buf/official C# plugins; initial generation and dependency restore need network access. Built-package consumers do not need Go or Buf. The example listens on loopback port 18000 with cleartext HTTP/2, publishes a complete listener/route/cluster/endpoint graph for node `node`, and reloads backend configuration. See its [instructions](examples/Envoy.ControlPlane.Example/README.md) and the [usage and migration guide](docs/usage.md).

## Packages and contracts

All maintained libraries target **net10.0**. The four root candidates use **1.0.0-rc.1** under the owner-selected `Knightfall.Envoy.ControlPlane` package family. Assembly names, C# namespaces and protobuf identities retain their existing names. These are locally built candidates, not published NuGet packages; see the [release checklist](docs/releases/root-1.0.md).

| NuGet package | Responsibility |
|---|---|
| [Knightfall.Envoy.ControlPlane.Protocol](src/Envoy.ControlPlane.Protocol/Envoy.ControlPlane.Protocol.csproj) | Authoritative generated messages, descriptors, original PGV definitions and gRPC contracts |
| [Knightfall.Envoy.ControlPlane.Validation.Annotations](src/Envoy.ControlPlane.Validation.Annotations/Envoy.ControlPlane.Validation.Annotations.csproj) | Shared derived Protovalidate annotation definitions; no evaluator |
| [Knightfall.Envoy.ControlPlane](src/Envoy.ControlPlane/README.md) | Caches, snapshots, protocol engines, Fetch, client and helpers |
| [Knightfall.Envoy.ControlPlane.AspNetCore](src/Envoy.ControlPlane.AspNetCore/README.md) | Kestrel/gRPC adapters, DI, endpoint mapping and optional HTTP gateway |
| [Knightfall.Envoy.ControlPlane.Validation](src/Envoy.ControlPlane.Validation/Envoy.ControlPlane.Validation.csproj) | Experimental source-built optional evaluator; outside the root release candidate |

Use [public API and lifetime contracts](docs/public-api.md) when implementing a custom cache, response or callback. Behavioral parity uses idiomatic C# channels, asynchronous ownership, `CancellationToken`, `TimeProvider` and ordinary ASP.NET Core APIs. It does not require identical Go APIs, hash bytes, error text or synchronization internals.

## Verify and maintain

After generation, the maintained CLI builds, tests and packs the solution, runs the pinned Go comparison corpora, and executes consumers of the newly built packages:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
dotnet run --project tools/Envoy.ControlPlane.Tools -- envoy verify
```

The second command requires Docker and runs the locked released-Envoy graph probe. Source comparisons, generated-client HTTP/2 tests and released-proxy interoperability establish different forms of evidence. The [test guide](tests/README.md) and [command map](tools/README.md) explain commands, artifacts and platform requirements. Normal CI verifies Linux/macOS/Windows; the Envoy workflow uses Linux amd64 and arm64. The accepted gRPC performance baseline runs only by manual dispatch.

| Evidence | Scope |
|---|---|
| [Provenance](schemas/PROVENANCE.md), [bindings](schemas/BINDINGS.md), [validation feasibility](schemas/VALIDATION.md) | Immutable inputs, generated ownership/fingerprints and bounded optional-validator research |
| [Parity matrix](docs/plan/PARITY-MATRIX.md), [traceability](docs/plan/TRACEABILITY.md) | Required root surface, scenarios and release gates |
| [T21 differential](docs/plan/T21-ROOT-DIFFERENTIAL.md) | Actual pinned Go comparisons and corruption-detection checks |
| [T22 lifecycle](docs/plan/T22-ROOT-LIFECYCLE.md) | Deterministic cleanup/progress and repeated HTTP/2 sessions |
| [T23 Envoy](docs/plan/T23-ROOT-ENVOY.md) | One released proxy, four-family traffic graph, updates/removals/reconnect and explicit Delta TTL |
| [T24 packaging and updates](docs/plan/T24-ROOT-READINESS.md) | Clean consumption, package boundaries, reproducibility and verification selection |

Source/tool locks, catalog and descriptor manifests are authoritative. The behavior reference is Go commit `67c5345367582741334e34ffc66a561b0ce32946`; the schema source is Envoy commit `82a4424f760b6ba3ffc9f2911389d061656f35c8`. See [upstream-baseline.json](upstream-baseline.json), [schemas/upstream.lock.json](schemas/upstream.lock.json) and the compatibility report for the independent proxy axis.

[AGENTS.md](AGENTS.md) describes contribution rules, centralized dependencies, Roslynator and verification. [The implementation plan](docs/plan/IMPLEMENTATION-PLAN.md) tracks remaining work. The [architecture](docs/architecture/ARCHITECTURE.md), [decisions](docs/architecture/DECISIONS.md) and [original proposal](PARITY-PLAN.md) supply design context; plans do not establish implementation or interoperability proof.
