# Envoy.ControlPlane.AspNetCore

NuGet package: `Knightfall.Envoy.ControlPlane.AspNetCore` (hosting). The assembly remains `Envoy.ControlPlane.AspNetCore`.

Hosts the Core SotW and Delta engines through the authoritative generated gRPC contracts and Kestrel. ADS and CDS, EDS, LDS, RDS, SRDS, SDS, RTDS and ECDS support both streaming modes. VHDS and LEDS add their Delta-only RPCs. The eight standard services also support unary Fetch. Rate-limit configuration supports dedicated SotW `StreamRlsConfigs` and unary `FetchRlsConfigs`, with no dedicated Delta RPC. The HTTP gateway is opt-in.

```csharp
using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server => server.ListenLocalhost(18000,
    endpoint => endpoint.Protocols = HttpProtocols.Http2));
var cache = new SnapshotCache();
builder.Services.AddSingleton<IConfigWatcher>(cache);
builder.Services.AddSingleton(new SotwOptions { OrderedAds = false });
builder.Services.AddEnvoyControlPlane();
var application = builder.Build();
application.MapEnvoyControlPlane();
await application.RunAsync();
```

This example binds cleartext HTTP/2 on loopback. Configure TLS, certificates, message sizes, connection settings and shutdown through ordinary ASP.NET Core/Kestrel settings. `AddEnvoyControlPlane` calls `AddGrpc`; application `AddGrpc` option configuration remains available. No transport quotas or authentication provider are added by this library.

Register a shared singleton `IConfigWatcher` and optional singleton `ISotwCallbacks`/`SotwOptions` and `IDeltaCallbacks`/`DeltaOptions`. `AddEnvoyControlPlane` retains application registrations and uses separate singleton Core servers with independent per-call sessions. Stream IDs are local to each engine. An instance supplied with `AddSingleton<IConfigWatcher>(cache)` remains application-owned. Factory-created service ownership follows the normal DI container rules. The adapter does not dispose caches or client channels.

`MapEnvoyControlPlane` returns a standard route group. To protect it, register the policy and the application's authentication provider **before** `builder.Build()`:

```csharp
builder.Services.AddAuthorization(authorization => authorization.AddPolicy("xds",
    policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "xds")));
// Register the application's authentication provider here as well.
```

After `builder.Build()`, configure the middleware and map the protected group instead of the unprotected mapping above:

```csharp
application.UseAuthentication();
application.UseAuthorization();
application.MapEnvoyControlPlane().RequireAuthorization("xds");
```

 Node ID is a cache routing key, not an authenticated identity. The open callback's `ResponseContext.State` is the `ServerCallContext`; use `GetHttpContext().User` for application identity. Existing request callbacks can reject with `RpcException`. Retain any per-stream authorization data by stream ID and release it in the close callback. SotW response callbacks receive the cache publication context, which can differ from the open context. Delta response callbacks follow their separate source signature and do not receive that context.

The transport forwards the framework asynchronous request reader and serial writer to `SotwServer` or `DeltaServer`. Call cancellation and host `ApplicationStopping` reach the Core session; its cleanup settles owned reads/watches and invokes close. Kestrel's remaining graceful connection drain follows `HostOptions.ShutdownTimeout`. The library supplies no separate cleanup timer. A dedicated streaming RPC accepts an omitted type or the endpoint's own type and rejects a different type with `InvalidArgument`, including after request callback mutation. ADS accepts custom type URLs within the configured `MaxAdsResourceTypes` limit.

The first stream failure determines the public status when processing and cleanup both fail. The adapters preserve application `RpcException` statuses and trailers and map cancellation exceptions to `Cancelled` when the call or host is stopping. Unexpected SotW and Delta failures return fixed `Internal` details. The host logs unexpected exceptions and aggregates with cleanup failures.

`ILogger` records stream open/close and protocol status at debug level, without resource payloads. Configure logging providers and gRPC logging through the host. Protocol/Core keep their existing dependency boundary, and no optional validation/CEL dependency is introduced.

The repository's `examples/Envoy.ControlPlane.Example` shows a configuration-driven HTTP graph update source with listener/HCM/router/route/cluster/endpoint resources. `docs/plan/T11-ASP-NET-HOST.md` records the original SotW HTTP/2 tests and package-consumer evidence; `docs/plan/T14-DELTA-GRPC.md` records all eleven Delta RPCs and their transport tests. `docs/plan/T23-ROOT-ENVOY.md` separately records released-Envoy SotW/Delta traffic, removal/reconnect and explicit Delta TTL proof on Linux amd64/arm64, with its four-family graph and known SotW CDS heartbeat limitation. These checks establish no new performance measurements. The accepted transport performance baseline runs only by manual dispatch.

## Fetch and optional HTTP gateway

The shared Core `FetchServer` uses an explicitly registered singleton `IConfigFetcher`, or the registered watcher when it also implements that capability (including `SnapshotCache` and `MuxCache`). A watcher-only producer continues streaming and reports unsupported Fetch; LinearCache retains its unsupported result. Optional `IFetchCallbacks` run for both transports. The gRPC adapter sets the service's type before the request hook and preserves application `RpcException` statuses and trailers. Unary contexts carry `ServerCallContext` and combined call/host cancellation. A cancellation exception during call cancellation or host shutdown maps to `Cancelled`. Other failures, including unchanged versions, keep the `Unknown` status with a fixed public detail. The host logs the original exception.

After `AddEnvoyControlPlane`, opt in separately at the application root:

```csharp
application.MapEnvoyControlPlaneHttpGateway().RequireAuthorization("xds");
```

The fallback endpoint cleans paths and handles `/v3/discovery:endpoints`, `clusters`, `listeners`, `routes`, `scoped-routes`, `secrets`, `runtime` and `extension_configs`. Ordinary application endpoints take precedence. No rate-limit/ADS/VHDS/LEDS HTTP route is added. Every method may supply a JSON body; content type does not select a codec. The selected route overrides the body's type before callbacks. JSON responses preserve protobuf field names. An unchanged version (including an ordinary wrapped `SkipFetchException`) returns an empty 304; bad/read-failed JSON is 400, unknown paths 404 and Fetch/materialization/format failures 500. Successful responses use `application/json`; error bodies are empty and server exception details stay in debug logs.

Google.Protobuf supplies parsing/formatting. The default Any registry includes the eight resource roots, their descriptor dependencies and discovery TTL wrappers. For opaque extension payloads, supply a **complete** registry containing those roots/wrappers plus application extension descriptors, using `Google.Protobuf.Reflection.TypeRegistry.FromMessages`/`FromFiles`. A Listener descriptor alone cannot resolve an HCM payload hidden in Any; include `HttpConnectionManager.Descriptor`. No global reflection registry or alternate JSON implementation is installed. Unknown Any formatting fails with 500.

Kestrel and upstream infrastructure can normalize or escape URLs before the gateway sees `Request.Path`. All gateway aliases share the fallback endpoint's authorization metadata. Authorization middleware runs before the gateway applies its own slash and dot cleaning. Apply authorization to the mapped fallback and use the application's authenticated identity to authorize the effective resource type and node in the Fetch hook when permissions vary by resource. Fetch hooks are trusted application code and can change the request that reaches the cache. Keep proxy path rules consistent with the gateway's path handling. Authentication schemes that use ambient credentials need the application's usual cross-site request protections.

The gateway limits request bodies to 1 MiB by default, including requests without a declared length. Use `MapEnvoyControlPlaneHttpGateway(maxRequestBodyBytes: ...)` to configure a positive limit. The gateway returns 413 on excess data. Host limits still apply, and a host `BadHttpRequestException` keeps its status when the gateway can send a response. Other body read failures return 400. Host shutdown returns an empty 503 if the response can still start, including when a concurrent body read or Fetch failure occurs. A disconnect can end a request before a status can be sent. Strict UTF-8 decoding precedes the stock protobuf parser.

## Supply-chain verification

Find the matching package and its release evidence on [GitHub Releases](https://github.com/knightfall/envoy-dotnet-control-plane/releases). The package's `security/provenance.json` identifies verification locations; it is not itself an attestation. Follow the [package verification guide](https://github.com/knightfall/envoy-dotnet-control-plane/blob/main/docs/releases/VERIFYING.md) to verify the release artifact and compare it with a NuGet download.
