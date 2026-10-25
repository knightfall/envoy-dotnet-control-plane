# Envoy.ControlPlane.AspNetCore

Hosts the Core SotW engine through the authoritative generated gRPC contracts and Kestrel. The preview implements SotW ADS and CDS, EDS, LDS, RDS, SRDS, SDS, RTDS and ECDS streams. Delta and Fetch retain their generated `Unimplemented` responses; VHDS, LEDS and rate-limit configuration services are not mapped by this preview.

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

Register a shared singleton `IConfigWatcher` and optional singleton `ISotwCallbacks` and `SotwOptions`. `AddEnvoyControlPlane` retains application registrations and uses one singleton Core server with independent per-call sessions. An instance supplied with `AddSingleton<IConfigWatcher>(cache)` remains application-owned. Factory-created service ownership follows the normal DI container rules. The adapter does not dispose caches or client channels.

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

Use this protected mapping instead of the unprotected mapping above. Node ID is a cache routing key, not an authenticated identity. The open callback's `ResponseContext.State` is the `ServerCallContext`; use `GetHttpContext().User` for application identity. Existing request callbacks can reject with `RpcException`. Retain any per-stream authorization data by stream ID and release it in the close callback. Response callbacks continue to receive the cache publication context, which can differ from the open context.

The transport forwards the framework asynchronous request reader and serial writer to `SotwServer`. Call cancellation and host `ApplicationStopping` reach the Core session; its cleanup settles owned reads/watches and invokes close. Kestrel's remaining graceful connection drain follows `HostOptions.ShutdownTimeout`. The library supplies no separate cleanup timer. Core `InvalidArgument` and `Unavailable` failures become corresponding gRPC statuses; application `RpcException` statuses are retained, and unexpected failures follow the framework's normal handling.

`ILogger` records stream open/close and protocol status at debug level, without resource payloads. Configure logging providers and gRPC logging through the host. Protocol/Core keep their existing dependency boundary, and no optional validation/CEL dependency is introduced.

The repository's `examples/Envoy.ControlPlane.Example` shows a configuration-driven endpoint update source. `docs/plan/T11-ASP-NET-HOST.md` records actual HTTP/2 tests and package-consumer evidence. These checks do not establish released-Envoy interoperability, full xDS parity or new performance measurements.
