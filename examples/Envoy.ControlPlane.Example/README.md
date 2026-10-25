# Minimal SotW host and application update source

From the repository root, after generating bindings:

```sh
dotnet run --project examples/Envoy.ControlPlane.Example
```

The host listens on loopback port 18000 with cleartext HTTP/2. Use node ID `node` and request EDS resource `service`, or request that endpoint type through ADS. The application initially publishes `127.0.0.1:8080`. Change `BackendPort` in `appsettings.json` while it runs; the normal configuration reload and application-owned polling source publish a replacement snapshot with a new opaque version. Existing requests receive it through the same Core engine. `NodeId` selects the application cache key at startup.

`BackendPublisher` is an ordinary `BackgroundService` with its own `PeriodicTimer` and shutdown token. It constructs stable replacement protobuf messages and awaits `SetSnapshotAsync`; it does not mutate previously published data. Its local revision counter demonstrates opaque versions rather than a persistence/recovery scheme. The timer is an application update source, separate from the library's deferred TTL/heartbeat work.

The application uses ordinary `ILogger` logging. For an authenticated host, configure an application authentication provider and protect the returned endpoint group with `RequireAuthorization`, as shown in the hosting package readme. Existing callbacks can apply node/claim checks. The library does not supply credentials, an authentication provider or an authorization policy.

This is an endpoint-discovery example. It does not bootstrap a complete Envoy listener/cluster/route graph; released-Envoy traffic and reconnect acceptance remain T12/T22/T23 work.
