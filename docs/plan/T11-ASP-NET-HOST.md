# T11 — ASP.NET Core SotW streaming host

Implements [issue #12](https://github.com/knightfall/envoy-dotnet-control-plane/issues/12) using the T09/T10 channel-based Core engine, generated upstream contracts, `Grpc.AspNetCore.Server` 2.84.0 and Kestrel. The behavior pin remains `67c5345367582741334e34ffc66a561b0ce32946`. No schema, generator, descriptor or generated-code fingerprint changes are needed.

## Hosting design

`AddEnvoyControlPlane` registers standard gRPC services, default SotW options and one shared Core server. Applications register a singleton `IConfigWatcher`, optional singleton `ISotwCallbacks` and optional initialized `SotwOptions`. Per-call state remains in Core sessions. `TryAddSingleton` preserves application registrations; cache ownership follows standard DI registration semantics. The library never receives or owns client channels.

`MapEnvoyControlPlane` maps thin generated-base adapters for SotW ADS and the eight standard services. All adapters call one small transport bridge that forwards `ReadAllAsync`, awaited writes, call cancellation and `ServerCallContext` to Core. Core gets the host's `ApplicationStopping` token. The public mapping returns a standard route group for authorization/conventions, rather than a custom endpoint builder or policy framework.

The generated contracts map 26 method endpoints across these nine services: nine implemented SotW methods and seventeen default-unimplemented Delta/Fetch methods. Preview support is explicitly limited to the nine SotW methods. VHDS, LEDS and rate-limit configuration service contracts are not mapped. Applications still use the authoritative Protocol types; no separate xDS bindings or server implementation is introduced.

Core protocol errors map to `InvalidArgument` or `Unavailable`. Application `RpcException` keeps its status; unexpected failures use normal gRPC framework handling. Debug `ILogger` messages contain stream type/status, without resource payloads. Existing in-path callbacks remain the application extension points, with the open context's state set to `ServerCallContext`; response contexts still come from cache publication.

Authentication, TLS, transport limits and graceful shutdown remain standard host concerns. Authorization conventions apply to the whole returned route group. Node ID remains a routing key, rather than identity proof. The host tests use an explicitly test-only authentication handler to exercise the standard authentication/authorization pipeline; it is not shipped in the library/example. The readme shows how an application supplies its provider and policy.

## Cancellation and shutdown

Call cancellation reaches the framework reader and Core linked token. Host stopping cancels Core, settles watches/reader and invokes close once. Kestrel can continue its graceful connection drain while a client leaves its request side open; `HostOptions.ShutdownTimeout` controls that stage. The fixture configures a two-second standard host timeout and asserts the Core close/watch cleanup before waiting for host shutdown. The maintained test establishes eventual close/watch cleanup, bounded stop and caller-cache ownership; it does not independently prove cleanup preceded the transport timeout, since StopAsync is already running. The ApplicationStopping link is also checked in source review, and Core cancellation has its existing native tests. No product cleanup timer, connection-abort wrapper or detached reader worker was added.

The initial shutdown test timed out at its ten-second diagnostic bound. Diagnostics established that `ApplicationStopping` was canceled, close had run and watches were already zero; the remaining wait belonged to graceful Kestrel drain. [The pinned gRPC asynchronous reader](https://github.com/grpc/grpc-dotnet/blob/v2.84.0/src/Grpc.Net.Common/AsyncStreamReaderExtensions.cs) combines enumerator cancellation, and [its server reader](https://github.com/grpc/grpc-dotnet/blob/v2.84.0/src/Grpc.AspNetCore.Server/Internal/HttpContextStreamReader.cs) forwards cancellation to the pipe reader. The implementation retains these existing APIs rather than duplicating them. Configuring the ordinary host timeout makes the bounded shutdown test match the actual hosting contract.

## Requirement coverage

| Requirement | Exact test evidence |
|---|---|
| V28: real HTTP/2 and every implemented preview service | `PreviewService_RoundTripsSnapshotOverHttp2AndClosesItsWatch` — nine generated clients, typed resource bytes, implied/explicit URLs, versions/nonces and HTTP/2 context |
| V28/V29: update, ACK/NACK and shared Core engine in both modes | `Ads_AckUpdateAndNackUseTheSameCoreStateOverHttp2` |
| V29: application option registration reaches the engine | `RegisteredOptions_ControlLegacyWildcardBehavior` |
| V28: precise advertised service/method inventory | `EndpointInventory_ContainsOnlyTheNinePreviewServiceContracts`; `DeferredMethods_RetainGeneratedUnimplementedStatus` |
| V15/V29: protocol status mapping and application rejection | `CoreRejection_MapsStatusAndClosesTheCall` — missing ADS URL, terminal watch and callback `RpcException` |
| V15/V29: call cleanup, shared server identity and caller channel ownership | `ClientCancellation_ReleasesWatchAndLeavesCallerChannelUsable` — a second service call succeeds and stream IDs advance |
| V15/V29: host cancellation, close once and caller cache ownership | `HostShutdown_ClosesPendingCallWithoutDisposingCallerCache` |
| Application authentication/authorization through host facilities | `StandardHostAuthorization_AppliesToMappedGrpcGroup` — unauthenticated, forbidden and allowed calls |
| Build/discovery and clean package consumption | Existing solution entry point includes the hosting test project and example; `repository verify` runs all hosting tests again against a freshly packed package |

Tests use real ephemeral-loopback Kestrel listeners and generated `Grpc.Net.Client` clients, rather than direct adapter calls or an in-memory HTTP substitute. They exercise framework implementations already used by the transport probe. Test code only supplies lifecycle observation and the application authentication fixture; it does not reimplement the protocol. The existing attributed Go fixture/oracle and sixteen Core stream traces remain unchanged and run in repository verification.

## Example, packaging and evidence

`examples/Envoy.ControlPlane.Example` is a maintained, non-packable web project. Its application-owned `BackendPublisher` reads ordinary configuration and publishes stable endpoint replacements with opaque versions. The example timer is separate from deferred library heartbeats. A separate generated EDS client smoke test against the running example observed backend 8080 changing to 8081 after a temporary configuration reload, versions `example/1` to `example/2`, nonces 1 to 2, and a clean stream end; Ctrl+C then stopped the host normally. This manual example check is separate from the 23 maintained test cases. Its readme states the endpoint-only scope and application authentication configuration.

The existing CPM pins gRPC server/client at 2.84.0. Only hosting gains `Grpc.AspNetCore.Server` and `Grpc.Net.Common`; Protocol/Core keep the prior package graph, and optional validation/CEL stays separate. Locks, Roslynator, the SDK pin and five-library packaging remain in force. The executable ASP.NET package consumer now registers the engine and maps its services; a fresh package-only hosting test consumer executes all 23 real HTTP/2 tests.

Local macOS arm64 `repository verify` passes: 222 solution tests, zero failed/skipped, zero build warnings/errors; all five package/symbol pairs; fresh Protocol (12), Validation (18), Core (144) and Hosting (23) test consumers; executable Core/ASP.NET consumers. Existing Go corpora match: 33 PGV, 19 snapshot, 22 cache, 14 subscription and 16 stream cases. Source SDK is .NET 10.0.400; Go is 1.26.4. Exact-head remote CI and Astra medium review belong to the implementing PR.

This establishes the preview transport slice of V15/V28/V29 on local HTTP/2. TLS configuration remains in Kestrel; the prior transport investigation has separate TLS/probe evidence. Released-Envoy configuration acceptance, traffic, reconnect and complete preview gates remain T12/T22/T23. Delta, Fetch, rate-limit configuration hosting and full validation remain later tasks. No new performance result or NuGet publication is claimed; the earlier gRPC/Kestrel performance penalty remains accepted.
