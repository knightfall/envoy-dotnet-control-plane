# Envoy.ControlPlane resources and snapshots

Core provides resource entries, name/type mapping, partial snapshots, EDS/RDS reference checks and explicit per-resource Delta versions. It references the authoritative Protocol bindings and has no hosting or optional validation/CEL dependency. Caches, watches and streaming services remain later implementation tasks.

```csharp
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;

var listener = new Listener { Name = "listener", StatPrefix = "http" };
var snapshot = new Snapshot("config/v1", new Dictionary<string, IEnumerable<ResourceEntry>>
{
    [ResourceTypeUrls.Listener] = [new(listener, TimeSpan.FromSeconds(30))],
});

snapshot.EnsureConsistent(); // Optional EDS/RDS reference-set check.
snapshot.ConstructVersionMap(); // Explicit, cached Delta hashes.
string version = snapshot.GetVersion(ResourceTypeUrls.Listener);
string hash = snapshot.GetVersionMap(ResourceTypeUrls.Listener)!["listener"];
```

`ResourceCollection` indexes names with the last duplicate winning. Endpoint assignments use `ClusterName`; other generated messages use their protobuf string `name` field. `INamedResource` supports application naming. The built-in snapshot supports twelve known resource families; applications can implement `IResourceSnapshot` for arbitrary type URLs and use `Google.Protobuf.Reflection.TypeRegistry` for custom Any descriptors.

Messages must remain stable while retained. Publish replacement messages and snapshots when configuration changes. The library does not clone messages or guarantee mutation isolation. `GetResources` creates a fresh outer name index; `GetResourcesAndTtl` exposes the retained index through a read-only interface. Keep both retained messages and collection contents stable.

Type versions are caller-supplied opaque strings, compared for equality. An absent group returns a null resource index and empty version; an explicitly empty group retains its supplied version. The collection-based constructor supports independent versions per family. `GetVersionMap` returns null until `ConstructVersionMap` is called. Hashes use SHA256 over deterministic protobuf bytes; literal equality with Go hashes is not guaranteed across protobuf runtimes. TTLs and opaque type versions do not affect resource hashes.

Construction neither validates protobuf rules nor checks consistency. `EnsureConsistent` checks exact EDS/RDS resource-name sets, including listener default/standalone chains and scoped-route references. It does not establish Envoy runtime acceptance. Optional TTLs preserve absent versus zero values; .NET `TimeSpan` has 100 ns precision.

Behavior derives from the pinned upstream resource/cache implementation recorded in `upstream-baseline.json`. The package includes the upstream Apache-2.0 license. Repository evidence and source links are in `docs/plan/T05-RESOURCES-AND-SNAPSHOTS.md`.
