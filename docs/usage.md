# Hosting, configuration updates and migration

The root library targets .NET 10. Package publication and final candidate identities remain tracked in the [release checklist](releases/root-1.0.md). These instructions describe the current source and freshly packed local packages.

## Run the maintained application

Generate bindings and start the example from the repository root:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <source-cache>
dotnet run --project examples/Envoy.ControlPlane.Example
```

Use an external writable source cache. The pinned source/tool retrieval and remote plugins need network access. Generated files live under `obj/`; consuming the resulting packages requires neither Buf nor Go.

The [example](../examples/Envoy.ControlPlane.Example/README.md) listens on loopback port 18000 with cleartext HTTP/2. Node `node` receives listener `listener`, route configuration `routes`, EDS cluster `service` and endpoint assignment `service`. The resource graph describes an Envoy listener on port 10000 forwarding to `127.0.0.1:8080`; the management host itself opens neither of those application ports. A running Envoy and backend are separate application processes. `envoy verify` supplies an isolated, reproducible proxy/backend test with locked images.

Change `BackendPort` in the example's `appsettings.json`. The application-owned `BackendPublisher` reloads configuration, builds replacement protobuf messages and awaits `SnapshotCache.SetSnapshotAsync`. The local version counter is an example update source, without persistence or recovery policy. `NodeId` selects the cache key at startup. `ListenerPort` changes the advertised Envoy resource.

## Add a management endpoint

A host registers its cache as a singleton watcher, then uses the ordinary ASP.NET Core registration and endpoint APIs:

```csharp
using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server => server.ListenLocalhost(18000,
    endpoint => endpoint.Protocols = HttpProtocols.Http2));
var cache = new SnapshotCache();
builder.Services.AddSingleton<IConfigWatcher>(cache);
builder.Services.AddEnvoyControlPlane();
var application = builder.Build();
application.MapEnvoyControlPlane();
await application.RunAsync();
```

This opens the management endpoint; an application must separately publish snapshots. Use the maintained example for a complete update source. `MapEnvoyControlPlane` maps all supported gRPC services. The returned route group supports `RequireAuthorization` and other ordinary endpoint conventions. Configure TLS, an authentication provider, body/message limits, deadlines and shutdown through Kestrel, ASP.NET Core and gRPC. Node ID routes cache data; it is not an authenticated identity. See the [hosting guide](../src/Envoy.ControlPlane.AspNetCore/README.md) for the callback context and a protected mapping.

The optional HTTP gateway is a separate mapping:

```csharp
application.MapEnvoyControlPlaneHttpGateway();
```

Apply the application's authorization policy to that mapping too. It exposes the eight [discovery paths](plan/T18-FETCH-GATEWAY.md) and protobuf JSON status semantics. Register complete descriptors for any opaque extension payloads; the default registry cannot discover arbitrary messages hidden inside `Any`. Preserve the default resource/wrapper roots when replacing its registry. See the hosting guide for configuration and error behavior.

## Publish stable snapshots

Construct a replacement graph using the authoritative generated messages and `ResourceEntry`, optionally call `EnsureConsistent`, and await `SetSnapshotAsync` with the node's cache key. The [example resource factory](../examples/Envoy.ControlPlane.Example/ExampleResources.cs) constructs the full four-family graph. Reference consistency checks only the selected upstream EDS/RDS sets. It establishes neither protobuf-rule validity nor proxy acceptance.

Messages, requests and retained snapshot indexes must stay stable while the library retains them. A new version string without a changed payload does not change a Delta resource hash. Change payloads by creating replacements, then publish a new snapshot. Type versions are opaque strings; avoid using them as a cross-runtime content-hash contract. Successful publication means eligible responses have been enqueued; it does not mean Envoy ACKed them or applied a cross-type transaction.

Select cache readiness with `new SnapshotCache(ads: true)` and ordered SotW ADS with a registered `SotwOptions { OrderedAds = true }`. Both default to false and solve separate source behaviors. Legacy initial empty-request wildcard behavior defaults to enabled in both stream engines. See [the API contract](public-api.md) before changing those options.

TTL wrapping does not expire entries in the cache. `RunHeartbeatsAsync` is an explicit caller-owned SotW loop; cancel and await it when shutting down. The released Envoy 1.40.1 default SotW CDS mux removes clusters on payload-free heartbeats. The [interop report](plan/T23-ROOT-ENVOY.md) records that limitation and the separately proven caller-supplied Delta TTL path. The library provides no automatic Delta heartbeat loop.

## Migrate an existing application

| Existing approach | Current .NET contract |
|---|---|
| An older Envoy generated-message package | Replace overlapping bindings with Protocol and its annotation dependency. Check the pinned catalog and required extensions before changing references. Loading two providers for the same protobuf definitions breaks the authoritative-owner contract. |
| Go caches and goroutines | Retain the observable subscription/publication semantics, using asynchronous publication and channel-based one-shot watches. Await owned asynchronous disposal before changing subscription state. |
| Awaiting one response | Use the two-argument watch convenience extension and await its completion. Custom producers implement the channel-writer methods on `IConfigWatcher`. |
| Automatic consistency or semantic validation assumptions | Invoke the desired checks explicitly before publication. Full optional-validator conformance remains T26/GV work. |
| Existing preview host | Existing registration/mapping remain; the mapping now includes Delta, Fetch, VHDS/LEDS and rate-limit configuration methods. Protect the endpoint group according to the application's policy. |
| Custom gRPC channels | Pass the application-owned invoker to `SotwAdsClient`; dispose the helper and await pending operations. Reconnect, decoding and acceptance policy stay in the application. |

Protocol offers generated C# and wire contracts for the selected catalog. It does not promise source or binary replacement for every older NuGet package: compare public namespaces, assembly identity, target framework, generated naming corrections and schema revisions. The [binding report](../schemas/BINDINGS.md) records the three generator naming corrections without changing protobuf identities. Custom resources can use `IResourceSnapshot`, `IMessage`/`Any`, and explicit descriptor registration instead of modifying the standard catalog.

For an upstream/schema update, use `upstream report` and `upstream diff` as documented in the [tool guide](../tools/README.md). Preserve old evidence, rerun the selected gates against the new inputs, and treat unevaluated validator rules as an open validation gate.
