# SotW preview

The preview supplies .NET 10 bindings, SnapshotCache, eight standard SotW discovery streams and SotW ADS through ASP.NET Core gRPC/Kestrel. Its listener/HCM/router/route/cluster/endpoint example has released-Envoy traffic, live update, NACK/repair and reconnect proof. [compatibility.json](compatibility.json) records exact packages, source/tool/binding pins, RPCs, defaults, intentional .NET differences and immutable evidence commits. This is a historical milestone record. The later [1.0.0-rc.1 release](../releases/1.0.0-rc.1.md) published five NuGet packages; T12's implementing PR records the preview review/CI gate.

This is the accepted **SotW preview** record. Current source additionally implements [Delta cache/streams and eleven gRPC methods](../plan/T14-DELTA-GRPC.md); that later HTTP/2 evidence does not expand the preview's released-Envoy acceptance. The manifest's `outsidePreview` list describes its acceptance boundary, not current source availability.

## Reproduce and run

From a clone's root, use SDK **10.0.400** and Go **1.26.4**. Initial generation needs network access and a writable external source cache; no separate source checkout or global Buf installation is required. Consumers of compiled packages need .NET 10, without Go or Buf.

```sh
dotnet restore tools/Envoy.ControlPlane.Tools --locked-mode
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache /path/to/source-cache
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
dotnet run --project examples/Envoy.ControlPlane.Example
```

Generation retrieves pinned Buf and official plugins, preserves original PGV options and separately fingerprints derived metadata. Inputs/source stay under `obj/`. Avoid overlapping Envoy binding assemblies or `Google.Api.CommonProtos`; [binding evidence](../../schemas/BINDINGS.md) records ownership and the three necessary C# naming corrections.

The example listens on loopback HTTP/2 port 18000 for node `node`. Its `BackendPort` and `ListenerPort` settings select the backend endpoint and Envoy listener resource. Configuration reload publishes stable replacements and a new opaque version. The host does not start a backend or Envoy; see [the example readme](../../examples/Envoy.ControlPlane.Example/README.md).

The documented source-example command was smoke-tested locally on macOS arm64 during T12: it loaded the example content root, listened at `http://localhost:18000`, published `example/1` for `node`, and shut down with exit code zero. That startup check is separate from the complete traffic proof below.

For a complete isolated graph and traffic test, connect a Linux/macOS Docker client to a Linux amd64/arm64 engine and run:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools -- envoy verify
```

The command builds the actual example/probe projects, extracts the pinned official **Envoy 1.40.1** binary and runs management host, two backends and proxy with external networking disabled. Ten checks per ADS mode prove traffic, EDS/RDS changes, a rejected route's NACK/nonce/accepted version, retained config, explicit repair, management outage, same-process reconnect and cleanup. Library NACK handling performs no rollback; the fixture pauses its NACK callback while publishing the explicit repair.

The image lock separates proxy release/source/platform from schema revision. Its SDK image runs **.NET 10.0.11**, recorded by the probe. Local arm64 and CI amd64 passed. This is one release and a four-family plaintext graph; other advertised methods have generated-client HTTP/2 evidence rather than complete Envoy workloads.

## Use the published packages

The preview manifest records historical local candidate versions, not current package identities. The five packages are now available at prerelease version `1.0.0-rc.1` on NuGet.org and GitHub Packages. For example, a standalone Web SDK application can reference the hosting package:

```xml
<PackageReference Include="Knightfall.Envoy.ControlPlane.AspNetCore" Version="1.0.0-rc.1" />
```

Use [the hosting readme](../../src/Envoy.ControlPlane.AspNetCore/README.md) for the complete minimal host and [Core's readme](../../src/Envoy.ControlPlane/README.md) for snapshots and channel ownership. Core-only consumers have no ASP.NET framework, evaluator or CEL runtime dependency. Protocol's annotation-only dependency preserves contracts without evaluating them. To test newly built source packages instead, see the [consumer guide](../../tests/consumers/README.md).

## Behavior and ownership

Ordered ADS is off, legacy wildcard is on, and cache ADS readiness is opt-in. Readiness and ordering are independent and do not create an atomic transaction. Both stream modes use channels; the task adapter is an await-one convenience. Custom inputs, producers, writers and callbacks must cooperate with cancellation and settle owned asynchronous operations during disposal.

Keep retained messages and collection contents stable; publish replacements. `SetSnapshotAsync` does not deep-clone, validate protobuf rules, run consistency checks or wait for proxy ACK. Applications can explicitly call `EnsureConsistent` for EDS/RDS reference checks. Versions are opaque; universal Go/C# payload-hash equality is not promised. TTL fields and lazy Delta version-map primitives do not imply Delta delivery or heartbeat scheduling.

Call/host cancellation releases watches and invokes close once. Caller-supplied cache instances/channels retain ownership; factory-created services follow normal DI rules. Node ID is a cache key, not authentication. Use standard ASP.NET Core TLS, authorization, logging and transport settings. [T11](../plan/T11-ASP-NET-HOST.md) documents ownership and status mapping. The accepted gRPC/Kestrel performance difference is recorded in [the transport investigation](../plan/GRPC-KESTREL-VALIDATION.md); no capacity/throughput SLA follows from these tests.

## Acceptance evidence

| Preview scenarios | Evidence and limit |
|---|---|
| V01–V04 | T01–T03 source/catalog/Any/descriptor ownership and original/derived metadata; T24 notices and consumers. |
| V05–V07 | T05/T20 stable resources, versions, partial snapshots and explicit EDS/RDS consistency; 19 Go snapshot and eight declared helper cases plus the graph. |
| V08–V09 | T06/T07/T10 watch registration/publication/cancellation, independent readiness/order; 23 cache traces. |
| V10–V13 | T08–T10/T21 subscriptions, type selection, callbacks/nonces, ordered drains; 14 subscription and 22 actual Go stream traces. Injected stale-ACK association is rejected. |
| V14–V15 | T07/T09/T11/T22 SotW status, in-path callbacks, logging and close. Delta times/fetch hooks remain outside preview. |
| V28–V30 | T11/T22 real HTTP/2 across all nine methods, cancellation, failed writes, active callbacks/host stop and seeded 128-stream/512-response cleanup. No quota/fairness or memory-budget claim. |
| V31 | T23 released Envoy traffic/update/NACK/reconnect: 20 checks across ordinary/ordered ADS, observed arm64 and amd64. |
| V37–V38 | T24 candidate packages, four full fresh test consumers/two executable graph consumers, notices/symbols and update/rule reports with unproven validation coverage. |

Merged [PR #52](https://github.com/knightfall/envoy-dotnet-control-plane/pull/52) and [PR #53](https://github.com/knightfall/envoy-dotnet-control-plane/pull/53) each passed twelve checks on their verified heads and received independent Sol xhigh review. T24 passed 271 solution tests, zero failed/skipped and zero compiler/Roslynator warnings/errors. Fresh consumers passed Protocol 14, Validation 18, Core 149 and Hosting 31 cases; pack retains three intentional non-packable-project notices. The manifest records exact commits; this documentation does not relabel old runs as new-head execution.

CI retains reports, consumer locks, packages/symbols and separate live-proxy diagnostics for its configured retention. Re-run the commands when artifacts expire. [T21](../plan/T21-PREVIEW-DIFFERENTIAL.md), [T22](../plan/T22-PREVIEW-LIFECYCLE.md), [T23](../plan/T23-PREVIEW-ENVOY.md), [T24](../plan/T24-PREVIEW-READINESS.md) and [traceability](../plan/TRACEABILITY.md) map exact assertions and separate gates.

## Remaining scope

The accepted preview covers **SotW ADS/CDS/EDS/LDS/RDS/SRDS/SDS/RTDS/ECDS**. Current source also hosts eleven Delta methods, including VHDS/LEDS, with the separate T14 evidence linked above. Current source also provides nine unary Fetch methods, dedicated rate-limit SotW streaming and an optional eight-route HTTP gateway; see [T18 evidence](../plan/T18-FETCH-GATEWAY.md). These are later scope than the accepted SotW preview. External authorization, rate-limit decisions and other generated services remain outside this host. Descriptors alone do not implement a service.

At the preview milestone, T15–T20 and the broader root interoperability and release gates remained future work; the later [root compatibility report](../compatibility/root-1.0.md) records their evidence. The [T26 report](../plan/T26-VALIDATION-CONFORMANCE.md) separately records optional validation coverage and exceptions. Matcher and broader contrib/legacy catalogs remain deferred. TTL behavior and the caller-owned SotW heartbeat scheduler are described in the root report; automatic Delta heartbeat scheduling is not provided.
