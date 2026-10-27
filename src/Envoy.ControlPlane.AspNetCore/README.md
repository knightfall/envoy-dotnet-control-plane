# Envoy.ControlPlane.AspNetCore

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

`MapEnvoyControlPlane` returns a standard route group. After configuring the application's authentication provider, protect that group with the usual authorization APIs:

```csharp
builder.Services.AddAuthorization(authorization => authorization.AddPolicy("xds",
    policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "xds")));
// Register the application's authentication provider before Build().
// Then configure the ordinary middleware and endpoint policy:
application.UseAuthentication();
application.UseAuthorization();
application.MapEnvoyControlPlane().RequireAuthorization("xds");
```

Use this protected mapping instead of the unprotected mapping above. Node ID is a cache routing key, not an authenticated identity. The open callback's `ResponseContext.State` is the `ServerCallContext`; use `GetHttpContext().User` for application identity. Existing request callbacks can reject with `RpcException`. Retain any per-stream authorization data by stream ID and release it in the close callback. SotW response callbacks receive the cache publication context, which can differ from the open context. Delta response callbacks follow their separate source signature and do not receive that context.

The transport forwards the framework asynchronous request reader and serial writer to `SotwServer` or `DeltaServer`. Call cancellation and host `ApplicationStopping` reach the Core session; its cleanup settles owned reads/watches and invokes close. Kestrel's remaining graceful connection drain follows `HostOptions.ShutdownTimeout`. The library supplies no separate cleanup timer. Core `InvalidArgument` and `Unavailable` failures become corresponding gRPC statuses; application `RpcException` statuses are retained, and unexpected failures follow the framework's normal handling.

`ILogger` records stream open/close and protocol status at debug level, without resource payloads. Configure logging providers and gRPC logging through the host. Protocol/Core keep their existing dependency boundary, and no optional validation/CEL dependency is introduced.

The repository's `examples/Envoy.ControlPlane.Example` shows a configuration-driven HTTP graph update source with listener/HCM/router/route/cluster/endpoint resources. `docs/plan/T11-ASP-NET-HOST.md` records the original SotW HTTP/2 tests and package-consumer evidence; `docs/plan/T14-DELTA-GRPC.md` records all eleven Delta RPCs and their transport tests. `docs/plan/T23-ROOT-ENVOY.md` separately records released-Envoy SotW/Delta traffic, removal/reconnect and explicit Delta TTL proof on Linux amd64/arm64, with its four-family graph and known SotW CDS heartbeat limitation. These checks establish no new performance measurements. The accepted transport performance baseline runs only by manual dispatch.

## Fetch and optional HTTP gateway

The shared Core `FetchServer` uses an explicitly registered singleton `IConfigFetcher`, or the registered watcher when it also implements that capability (including `SnapshotCache` and `MuxCache`). A watcher-only producer continues streaming and reports unsupported Fetch; LinearCache retains its unsupported result. Optional `IFetchCallbacks` run for both transports. gRPC adapters set the service's type before the request hook, preserve application `RpcException` statuses and use the framework's normal handling for ordinary cache errors, including unchanged versions. Unary contexts carry `ServerCallContext` and combined call/host cancellation.

After `AddEnvoyControlPlane`, opt in separately at the application root:

```csharp
application.MapEnvoyControlPlaneHttpGateway().RequireAuthorization("xds");
```

The fallback endpoint cleans paths and handles `/v3/discovery:endpoints`, `clusters`, `listeners`, `routes`, `scoped-routes`, `secrets`, `runtime` and `extension_configs`. Ordinary application endpoints take precedence. No rate-limit/ADS/VHDS/LEDS HTTP route is added. Every method may supply a JSON body; content type does not select a codec. The selected route overrides the body's type before callbacks. JSON responses preserve protobuf field names. An unchanged version (including an ordinary wrapped `SkipFetchException`) returns an empty 304; bad/read-failed JSON is 400, unknown paths 404 and Fetch/materialization/format failures 500. Successful responses use `application/json`; error bodies are empty and server exception details stay in debug logs.

Google.Protobuf supplies parsing/formatting. The default Any registry includes the eight resource roots, their descriptor dependencies and discovery TTL wrappers. For opaque extension payloads, supply a **complete** registry containing those roots/wrappers plus application extension descriptors, using `Google.Protobuf.Reflection.TypeRegistry.FromMessages`/`FromFiles`. A Listener descriptor alone cannot resolve an HCM payload hidden in Any; include `HttpConnectionManager.Descriptor`. No global reflection registry or alternate JSON implementation is installed. Unknown Any formatting fails with 500.

Kestrel, routing middleware and clients can canonicalize/escape URLs before this handler sees `Request.Path`; the gateway then applies source-style slash/dot cleaning. Normal endpoint authorization and host body limits still apply. Host connection/body-read failures and cancellation can terminate a request before a library status can be sent; a body-read failure delivered to the gateway maps to 400. No global quota, method restriction, media-type rejection or parser-limit override is introduced. Strict UTF-8 decoding precedes the stock protobuf parser. Error text, JSON whitespace/property order and time precision are runtime/adapter differences; the bounded Go corpus compares semantic JSON, not byte formatting. See `docs/plan/T18-FETCH-GATEWAY.md` for observed proof and remaining full-matrix/interop gates.
