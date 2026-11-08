# .NET Envoy control plane

A .NET library for building an [Envoy](https://www.envoyproxy.io/) xDS management server. Publish configuration from your application and serve it to Envoy over gRPC using State of the World (SotW) or Delta discovery.

[![Upstream verification](https://github.com/knightfall/envoy-dotnet-control-plane/actions/workflows/upstream-verification.yml/badge.svg?branch=main)](https://github.com/knightfall/envoy-dotnet-control-plane/actions/workflows/upstream-verification.yml)
[![Released Envoy interoperability](https://github.com/knightfall/envoy-dotnet-control-plane/actions/workflows/envoy-interop.yml/badge.svg?branch=main)](https://github.com/knightfall/envoy-dotnet-control-plane/actions/workflows/envoy-interop.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Knightfall.Envoy.ControlPlane?label=NuGet)](https://www.nuget.org/packages/Knightfall.Envoy.ControlPlane)
[![GitHub release](https://img.shields.io/github/v/release/knightfall/envoy-dotnet-control-plane?include_prereleases)](https://github.com/knightfall/envoy-dotnet-control-plane/releases)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/knightfall/envoy-dotnet-control-plane/badge)](https://scorecard.dev/viewer/?uri=github.com/knightfall/envoy-dotnet-control-plane)
[![License](https://img.shields.io/github/license/knightfall/envoy-dotnet-control-plane)](LICENSE)

## Status and requirements

All five packages target **.NET 10** (`net10.0`) and are published as the prerelease **1.0.0-rc.1**. The library implements a selected v3 root-library surface of Envoy's Go control plane. See the [compatibility report](docs/compatibility/root-1.0.md) for the tested scope, source pins and limitations, and the [release record](docs/releases/1.0.0-rc.1.md) for published artifacts.

The library provides xDS infrastructure, not a complete control plane application. Your application builds and publishes resources and owns configuration sources, persistence, rollout decisions and authentication.

## Features

- **SnapshotCache** stores a snapshot per node; **LinearCache** manages a single resource type; **MuxCache** routes requests to child caches.
- State of the World (SotW) and Delta discovery, separate services and aggregated discovery service (ADS), callbacks and cancellation.
- ASP.NET Core gRPC hosting, unary Fetch and an optional HTTP discovery gateway.
- Generated Protocol messages and contracts for the pinned standard schema catalog, resource helpers and a focused SotW ADS client.
- TTL response envelopes, caller-owned SotW heartbeat scheduling and optional explicit schema validation.

See [Compatibility and limitations](#compatibility-and-limitations) for important boundaries.

## Installation

For an ASP.NET Core management server, add the hosting package to a .NET 10 project:

```sh
dotnet add package Knightfall.Envoy.ControlPlane.AspNetCore --version 1.0.0-rc.1
```

The hosting package brings in Core and Protocol transitively. Use `Knightfall.Envoy.ControlPlane` directly for transport-independent caches and protocol engines, and add `Knightfall.Envoy.ControlPlane.Validation` only when the application needs explicit semantic validation. See [Packages](#packages) and the [usage guide](docs/usage.md) for details.

## Quick start

Create a .NET 10 ASP.NET Core web application, add the hosting package above and replace its `Program.cs` with the following. It publishes a cluster for node `node` and maps the gRPC discovery services:

```csharp
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server => server.ListenLocalhost(18000,
    endpoint => endpoint.Protocols = HttpProtocols.Http2));
var cache = new SnapshotCache();
var cluster = new Cluster
{
    Name = "service",
    Type = Cluster.Types.DiscoveryType.Eds,
    ConnectTimeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)),
    EdsClusterConfig = new Cluster.Types.EdsClusterConfig
    {
        EdsConfig = new ConfigSource
        {
            Ads = new AggregatedConfigSource(),
            ResourceApiVersion = ApiVersion.V3,
        },
        ServiceName = "service",
    },
};
var snapshot = new Snapshot("v1", new Dictionary<string, IEnumerable<IMessage?>>
{
    [ResourceTypeUrls.Cluster] = [cluster],
});
await cache.SetSnapshotAsync("node", snapshot);
builder.Services.AddSingleton<IConfigWatcher>(cache);
builder.Services.AddEnvoyControlPlane();
var application = builder.Build();
application.MapEnvoyControlPlane();
await application.RunAsync();
```

Run the application with `dotnet run`. It listens for xDS requests on `localhost:18000`; a client using node ID `node` can request the `service` cluster. This is a **partial snapshot**: the cluster expects endpoint discovery, so it is not a complete configuration for routing traffic. The [maintained example](examples/Envoy.ControlPlane.Example/README.md) adds endpoints, routes, a listener and an update source.

The endpoint uses cleartext HTTP/2 on loopback for local development. For deployments, configure transport security and authorization as described in the [hosting guide](src/Envoy.ControlPlane.AspNetCore/README.md); a node ID is a cache key, not an authenticated identity.

## Run the example

Use the SDK specified in [global.json](global.json). A fresh checkout must generate bindings before building the example. Choose a writable source cache outside the checkout and replace `<source-cache>` with its path:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <source-cache>
dotnet run --project examples/Envoy.ControlPlane.Example
```

Generation retrieves immutable sources and pinned Buf and official C# plugins; initial generation and dependency restore need network access. Built-package consumers do not need Go or Buf.

The example listens on loopback port `18000` and publishes a listener, route, cluster and endpoint graph for node `node`. The listener describes an Envoy port of `10000` and a backend at `127.0.0.1:8080`. Envoy and the backend are separate processes; the management host opens neither application port. Changing `BackendPort` or `ListenerPort` in the example's `appsettings.json` publishes a replacement snapshot.

To exercise the graph with an isolated released Envoy and backend, run `dotnet run --project tools/Envoy.ControlPlane.Tools -- envoy verify` after generation. This requires Docker. The [example instructions](examples/Envoy.ControlPlane.Example/README.md) describe the resources and configuration; the [tool guide](tools/README.md) describes platform requirements and retained artifacts.

## Packages

| NuGet package | Responsibility |
|---|---|
| [Knightfall.Envoy.ControlPlane.Protocol](https://www.nuget.org/packages/Knightfall.Envoy.ControlPlane.Protocol) | Authoritative generated messages, descriptors, original PGV definitions and gRPC contracts |
| [Knightfall.Envoy.ControlPlane.Validation.Annotations](https://www.nuget.org/packages/Knightfall.Envoy.ControlPlane.Validation.Annotations) | Shared derived Protovalidate annotation definitions; no evaluator |
| [Knightfall.Envoy.ControlPlane](https://www.nuget.org/packages/Knightfall.Envoy.ControlPlane) | Caches, snapshots, protocol engines, Fetch, client and helpers |
| [Knightfall.Envoy.ControlPlane.AspNetCore](https://www.nuget.org/packages/Knightfall.Envoy.ControlPlane.AspNetCore) | Kestrel/gRPC adapters, dependency injection, endpoint mapping and optional HTTP gateway |
| [Knightfall.Envoy.ControlPlane.Validation](https://www.nuget.org/packages/Knightfall.Envoy.ControlPlane.Validation) | Explicit optional evaluator with a separate dependency graph and declared validation scope |

Package IDs use the `Knightfall.Envoy.ControlPlane` family; assembly names, C# namespaces and protobuf identities retain their existing names. See the [usage guide](docs/usage.md) for package references and examples.

## Compatibility and limitations

The selected surface includes 30 gRPC methods and eight optional HTTP discovery routes. Generated Protocol types cover the pinned standard catalog, including commonly used extensions. Broader contrib and legacy catalogs and the standalone xDS matcher are deferred.

Original PGV options are preserved. Semantic validation is explicit and optional, with pinned rule coverage and exceptions documented in the [validation conformance report](docs/plan/T26-VALIDATION-CONFORMANCE.md). Publishing a snapshot invokes neither a validator nor a consistency check. Retained messages and snapshot indexes must remain stable; publish replacement messages and snapshots for updates. Publication does not mean that Envoy has ACKed or applied the configuration.

The [released-Envoy report](docs/plan/T23-ROOT-ENVOY.md) documents the tested proxy and scenarios, including the default Envoy SotW CDS heartbeat limitation and the separately proven explicit Delta refresh and expiry path. Behavioral parity uses idiomatic C# APIs and does not require identical Go APIs, hash bytes, error text or synchronization internals. The [compatibility report](docs/compatibility/root-1.0.md) is authoritative for the full scope and evidence.

## Build and verification

After generating bindings as shown above, run the maintained repository verification from the checkout root:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
dotnet run --project tools/Envoy.ControlPlane.Tools -- envoy verify
```

The first command builds, tests and packs the solution, runs the pinned Go comparison corpora and executes consumers of the newly built packages. The second requires Docker and runs the locked released-Envoy graph probe. Source comparisons, generated-client HTTP/2 tests, clean package consumption and released-proxy interoperability are distinct forms of evidence. See the [test guide](tests/README.md) and [command map](tools/README.md) for prerequisites, artifacts and supported platforms.

Normal CI is configured to verify Linux, macOS and Windows; the Envoy workflow uses Linux amd64 and arm64. The accepted gRPC performance baseline runs only by manual dispatch. The [root compatibility report](docs/compatibility/root-1.0.md) links to the recorded differential, lifecycle, interoperability and packaging results. Source and tool locks are authoritative for their respective pins; proxy versions are an independent compatibility axis.

## Documentation and contributing

- [Usage and migration](docs/usage.md): package consumption, hosting, updates and migration.
- [Public API and lifetime contracts](docs/public-api.md): custom caches, responses, callbacks and ownership.
- [Architecture](docs/architecture/ARCHITECTURE.md) and [decisions](docs/architecture/DECISIONS.md): design and tradeoffs.
- [Compatibility report](docs/compatibility/root-1.0.md) and [validation conformance](docs/plan/T26-VALIDATION-CONFORMANCE.md): tested scope and exceptions.
- [Test guide](tests/README.md) and [tool commands](tools/README.md): verification and maintenance.
- [Contribution guidance](AGENTS.md): repository conventions, dependencies and verification.
- [Implementation plan](docs/plan/IMPLEMENTATION-PLAN.md): task definitions, historical status and deferred scope. Plans describe intent, not proof of implementation or interoperability.
- [Publishing process](docs/releases/PUBLISHING.md): protected release workflow, SBOMs and attestations.

## License

Apache License 2.0. See [LICENSE](LICENSE).
