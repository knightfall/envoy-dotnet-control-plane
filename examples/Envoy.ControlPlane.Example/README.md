# Minimal xDS host and application update source

From the repository root, after generating bindings:

```sh
dotnet run --project examples/Envoy.ControlPlane.Example
```

The management host listens on loopback port 18000 with cleartext HTTP/2. Use node ID `node`. Its snapshot supplies listener `listener`, route configuration `routes`, EDS cluster `service` and endpoint assignment `service`. The listener contains a typed HTTP connection manager and router; HCM and EDS configuration use ADS/V3 sources. Request those resources through SotW/Delta ADS or their individual supported services. The example maps all implemented gRPC endpoints; it does not opt into the HTTP gateway.

The initial backend is `127.0.0.1:8080`; the listener resource describes `127.0.0.1:10000`. `ListenerPort` configures that Envoy resource, rather than a socket opened by this application. Change `BackendPort` or `ListenerPort` in `appsettings.json`; normal configuration reload and the application-owned polling source publish stable replacement messages with a new opaque version. `NodeId` selects the cache key at startup.

`ExampleResources` constructs the four-family graph with existing generated messages, `Any.Pack`, canonical preview filter labels and an explicit `EnsureConsistent` check. This application check does not enable semantic validation in the cache.

`BackendPublisher` is an ordinary `BackgroundService` with its own `PeriodicTimer` and shutdown token. It constructs stable replacement protobuf messages and awaits `SetSnapshotAsync`; it does not mutate previously published data. Its local revision counter demonstrates opaque versions rather than a persistence/recovery scheme. This timer is an application update source. The separate caller-owned SotW heartbeat API and released-proxy limits are documented in [T17](../../docs/plan/T17-TTL-HEARTBEATS.md) and [T23](../../docs/plan/T23-ROOT-ENVOY.md); the example starts no heartbeat loop.

The application uses ordinary `ILogger` logging. For an authenticated host, configure an application authentication provider and protect the returned endpoint group with `RequireAuthorization`, as shown in the hosting package readme. Existing callbacks can apply node/claim checks. The library does not supply credentials, an authentication provider or an authorization policy.

The example constructs a complete HTTP resource graph. Run `dotnet run --project tools/Envoy.ControlPlane.Tools -- envoy verify` from the repository root after generation for the isolated released-Envoy traffic, update, NACK/repair, Delta removal/reconnect and explicit Delta TTL proof. The probe references this project and uses its compiled resource factory. The [root compatibility report](../../docs/compatibility/root-1.0.md) documents exact source/proxy pins and limits; the [tool guide](../../tools/README.md) describes Docker requirements and retained artifacts.

For diagnostics, `MessageConversions.ToStruct` can represent the constructed listener as a protobuf JSON object; `ToMessage<Listener>` creates a new listener from it. Supply `TypeRegistry.FromMessages(Listener.Descriptor, HttpConnectionManager.Descriptor, Router.Descriptor)` for the opaque HCM/router payloads. This optional conversion does not alter the publisher's stable-resource/update policy. The Core test `ExistingExampleGraph_RoundTripsRegisteredHcmAndRouterAny` exercises the actual example graph and preserves its RDS references.
