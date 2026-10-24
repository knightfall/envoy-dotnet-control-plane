# Repository commands

Run these C# file-based entry points from the repository root. Supporting code is included explicitly with `#:include`; the pinned .NET SDK supports this without another CLI project. Dependency versions come from `Directory.Packages.props`.

The root [.gitattributes](../.gitattributes) keeps tracked text files at LF on every platform, including Windows checkouts with `core.autocrlf=true`. Configuration and catalog fingerprints verify exact bytes; keep this checkout rule rather than changing the locked hashes to accommodate line-ending conversion.

| Entry point | Responsibility | Supporting code |
|---|---|---|
| [Upstream.cs](Upstream.cs) | Parse the requested source operation and coordinate provenance verification, inspection or export | [upstream/](upstream/) |
| [Bindings.cs](Bindings.cs) | Coordinate Buf generation and optional package verification | [bindings/](bindings/) |
| [PatchBindings.cs](PatchBindings.cs) | Apply only the exact, fingerprinted C# generator naming corrections | [binding lock](../schemas/bindings.lock.json) |

## Source verification

`upstream/` separates command options, repository/cache paths, baseline validation, archive retrieval, Go module inventory, schema catalog construction, descriptor inspection and verified export. `SchemaCatalog.Create` shows the indexing, root selection, import closure and document construction phases in order. `UpstreamData` contains the shared JSON/encoding/checksum operations; it does not choose schema policy.

```sh
dotnet run --file scripts/Upstream.cs -- sync --cache ../upstream-sources
dotnet run --file scripts/Upstream.cs -- inspect --cache ../upstream-sources --offline
```

The cache must be outside the implementation and upstream reference repositories. [Provenance evidence](../schemas/PROVENANCE.md) explains exact inputs and the inspection compiler's platform limit.

## Binding generation and package proof

`bindings/` separates Buf executable verification, generation/output verification, process execution and package-consumer verification. `BindingPaths` names the repository artifacts. `BindingIntegrity` provides the command's checksum and rejection operations.

```sh
dotnet run --file scripts/Bindings.cs -- generate --cache ../upstream-sources
dotnet run --file scripts/Bindings.cs -- verify --cache ../upstream-sources
```

`verify` restores and packs Protocol, runs all binding tests, then stages every handwritten test source (including `Support/`) in a temporary project that references the freshly built package. It excludes `bin/` and `obj/`, uses a fresh NuGet cache and records the consumer's lock/report before removing the temporary project. [Binding evidence](../schemas/BINDINGS.md) explains the compatibility checks and known generator corrections.

The upstream CLI test build target tracks `Upstream.cs` and every `upstream/*.cs` helper. Keep those inputs and entry-point includes current when adding or moving helpers, so local tests rebuild the implementation they exercise.
