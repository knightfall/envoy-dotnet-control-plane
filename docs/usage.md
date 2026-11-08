# Hosting, configuration updates and migration

The root library targets .NET 10. Five packages are published as prerelease `1.0.0-rc.1`; their identities and release evidence are in the [release record](releases/root-1.0.md). These instructions apply to the current source and the published package surface.

## Consume the published packages

The examples below target `net10.0` and use the published packages on NuGet.org. Reference Core directly for a transport-independent cache or stream engine:

```xml
<ItemGroup>
  <PackageReference Include="Knightfall.Envoy.ControlPlane" Version="1.0.0-rc.1" />
</ItemGroup>
```

For ASP.NET Core gRPC hosting, reference the hosting package. Its Core and Protocol dependencies resolve transitively:

```xml
<ItemGroup>
  <PackageReference Include="Knightfall.Envoy.ControlPlane.AspNetCore" Version="1.0.0-rc.1" />
</ItemGroup>
```

Add the separate Validation package only when the application wants explicit semantic validation. The application must also have access to the generated message package, for example through Core or an explicit Protocol reference:

```xml
<ItemGroup>
  <PackageReference Include="Knightfall.Envoy.ControlPlane.Validation" Version="1.0.0-rc.1" />
</ItemGroup>
```

Validation is caller-owned. For example, invoke it before publishing if that is the application's acceptance policy:

```csharp
using System;
using Envoy.Config.Listener.V3;

var listener = new Listener { Name = "listener" };
var result = new ProtoValidate.Validator().Validate(listener, failFast: false);

if (!result.IsSuccess)
{
    foreach (var violation in result.Violations)
    {
        Console.WriteLine($"{violation.Field}: {violation.Message} [{violation.RuleId}]");
    }
}
```

Constraint failures return a result containing violations. Constraint compilation or evaluation faults throw exceptions; they are not converted into violations. Core, `SnapshotCache` and hosting never invoke the evaluator automatically.

## Run the maintained application

Generate bindings and start the example from the repository root, replacing `<source-cache>` with a writable path outside the checkout:

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

### Add a custom resource family

For a custom family, generate a protobuf message whose field is named `name`; `ResourceCollection` uses that string field to index resources. For example, this schema produces the `Example.V1.Widget` type used below:

```proto
syntax = "proto3";
package example.v1;
option csharp_namespace = "Example.V1";

message Widget {
  string name = 1;
}
```

The following snapshot keeps a stable collection and read-only resource indexes, returns an empty type version and null indexes for other families, and constructs its per-resource Delta versions once from the same serialization and hash helpers used by the cache:

```csharp
#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Example.V1;
using Google.Protobuf;

public sealed class WidgetSnapshot : IResourceSnapshot
{
    public const string TypeUrl = "type.googleapis.com/example.v1.Widget";

    private readonly ResourceCollection collection;
    private readonly IReadOnlyDictionary<string, ResourceEntry> entries;
    private readonly IReadOnlyDictionary<string, IMessage?> resources;
    private readonly object versionMapLock = new();
    private IReadOnlyDictionary<string, string>? versionMap;

    public WidgetSnapshot(string typeVersion, IEnumerable<Widget> widgets)
    {
        collection = new ResourceCollection(typeVersion,
            widgets.Select(widget => new ResourceEntry(widget)));
        entries = new ReadOnlyDictionary<string, ResourceEntry>(
            collection.Items.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        resources = new ReadOnlyDictionary<string, IMessage?>(
            entries.ToDictionary(pair => pair.Key, pair => pair.Value.Resource, StringComparer.Ordinal));
    }

    public string GetVersion(string typeUrl) => typeUrl == TypeUrl ? collection.Version : "";

    public IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl) =>
        typeUrl == TypeUrl ? entries : null;

    public IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl) =>
        typeUrl == TypeUrl ? resources : null;

    public void ConstructVersionMap()
    {
        lock (versionMapLock)
        {
            versionMap ??= new ReadOnlyDictionary<string, string>(entries.ToDictionary(
                pair => pair.Key,
                pair => ResourceSerialization.ComputeHash(ResourceSerialization.Serialize(pair.Value.Resource)),
                StringComparer.Ordinal));
        }
    }

    public IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl) =>
        typeUrl == TypeUrl ? versionMap : null;
}

public static class Program
{
    public static async Task Main()
    {
        var widget = new Widget { Name = "primary" };
        var snapshot = new WidgetSnapshot("widgets-v1", new[] { widget });
        snapshot.ConstructVersionMap();

        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", snapshot);
    }
}
```

Type versions are opaque equality tokens; the per-resource versions above are deterministic payload hashes. Keep retained messages and indexes stable, and publish replacement messages and snapshots for updates. Delta cache paths also call `ConstructVersionMap`, so make that method idempotent and cache the result. The built-in `Snapshot` handles only its known resource families. Custom messages carried inside `Any` need their descriptors registered in the `TypeRegistry` used by JSON/reference conversion or hosting.

Select cache readiness with `new SnapshotCache(ads: true)` and ordered SotW ADS with a registered `SotwOptions { OrderedAds = true }`. Both default to false and solve separate source behaviors. Legacy initial empty-request wildcard behavior defaults to enabled in both stream engines. See [the API contract](public-api.md) before changing those options.

TTL wrapping does not expire entries in the cache. `RunHeartbeatsAsync` is an explicit caller-owned SotW loop; cancel and await it when shutting down. The released Envoy 1.40.1 default SotW CDS mux removes clusters on payload-free heartbeats. The [interop report](plan/T23-ROOT-ENVOY.md) records that limitation and the separately proven caller-supplied Delta TTL path. The library provides no automatic Delta heartbeat loop.

## Migrate an existing application

| Existing approach | Current .NET contract |
|---|---|
| An older Envoy generated-message package | Replace overlapping bindings with Protocol and its annotation dependency. Check the pinned catalog and required extensions before changing references. Loading two providers for the same protobuf definitions breaks the authoritative-owner contract. |
| Go caches and goroutines | Retain the observable subscription/publication semantics, using asynchronous publication and channel-based one-shot watches. Await owned asynchronous disposal before changing subscription state. |
| Awaiting one response | Use the two-argument watch convenience extension and await its completion. Custom producers implement the channel-writer methods on `IConfigWatcher`. |
| Automatic consistency or semantic validation assumptions | Invoke the desired checks explicitly before publication. The [T26 report](plan/T26-VALIDATION-CONFORMANCE.md) states the optional evaluator's tested scope and exceptions. |
| Existing preview host | Existing registration/mapping remain; the mapping now includes Delta, Fetch, VHDS/LEDS and rate-limit configuration methods. Protect the endpoint group according to the application's policy. |
| Custom gRPC channels | Pass the application-owned invoker to `SotwAdsClient`; dispose the helper and await pending operations. Reconnect, decoding and acceptance policy stay in the application. |

Protocol offers generated C# and wire contracts for the selected catalog. It does not promise source or binary replacement for every older NuGet package: compare public namespaces, assembly identity, target framework, generated naming corrections and schema revisions. The [binding report](../schemas/BINDINGS.md) records the three generator naming corrections without changing protobuf identities. Custom resources can use `IResourceSnapshot`, `IMessage`/`Any`, and explicit descriptor registration instead of modifying the standard catalog.

For an upstream/schema update, use `upstream report` and `upstream diff` as documented in the [tool guide](../tools/README.md). Preserve old evidence, rerun the selected gates against the new inputs, and treat unevaluated validator rules as an open validation gate.
