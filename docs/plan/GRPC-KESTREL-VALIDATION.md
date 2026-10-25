# gRPC/Kestrel transport validation

Scope: [issue #44](https://github.com/knightfall/envoy-dotnet-control-plane/issues/44). Validate gRPC for ASP.NET Core/Kestrel independently of xDS caches and protocol engines. The production hosting package remains a foundation; it does not yet register discovery endpoints. Linux results belong to the implementing PR and its exact head/run evidence.

## Stack choice

Use **Grpc.AspNetCore.Server 2.84.0**, Kestrel and the existing Buf-generated Protocol service bases. The new dependency belongs only to the non-packable test host. No production package dependency, binding owner or generated input changes.

The narrow server package avoids the [Grpc.AspNetCore metapackage's](https://github.com/grpc/grpc-dotnet/blob/4c6997a214601422dd66c5c74c1969db749479e9/src/Grpc.AspNetCore/Grpc.AspNetCore.csproj) `Grpc.Tools` and client-factory dependencies. Generation continues through Buf. The comparison uses **grpc-go 1.84.0** through the existing oracle module, with the root/Envoy revisions checked against behavior baseline `67c5345367582741334e34ffc66a561b0ce32946` and `-mod=readonly` builds.

## Reproduce

Generate contracts using the documented repository bootstrap, then run from the checkout root:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- transport verify
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- transport benchmark
```

The first command checks behavior; the second also measures performance. Linux and macOS are supported. The C# host is registered in the solution and uses CPM, a dependency lock and Roslynator. The Go driver shares the existing oracle module/locks. No custom schema or separate reference checkout is needed.

[The workflow](../../.github/workflows/transport-performance.yml) runs on relevant pull requests and supports manual dispatch once present on the default branch. It checks out the actual PR head, generates contracts through pinned Buf tooling and publishes a summary plus raw checks, trials, runtime/module details and process logs. Functional failures fail the job; performance has no numerical threshold.

Results live under `src/Envoy.ControlPlane.Protocol/obj/upstream/results/transport/`. The owned temporary executable/certificate directory is removed afterward. Test private keys are not uploaded; private-CA fixtures are not a production revocation policy.

## Functional evidence

Both servers implement the same generated ADS duplex and EDS unary contracts. One Go client drives both. Prebuilt opaque `Any` response bodies exercise real serialization/framing without xDS selection or materialization.

| Check | Concrete assertion |
|---|---|
| `DuplexHalfCloseUnaryAndMetadata` | Two exchanges, unary fields/size, clean EOF after half-close, response header/trailer and actual TLS version/cipher. |
| `ExplicitStatus` | Explicit `InvalidArgument` and application message survive transport. |
| `RequestSizeBoundaryAndLargeResponse` | Exactly 4,194,304 serialized request bytes pass; one additional byte returns `ResourceExhausted`. A 5 MiB response passes with the client receive cap raised explicitly. |
| `StreamingBodyBeyondHttpLimit` | One stream accepts 100 permitted messages totaling 52,429,200 serialized request bytes and closes cleanly. |
| `UnknownMethod` | A missing method returns `Unimplemented`. |
| `CancellationAndDeadlineCleanup` | Explicit cancel returns `Canceled`, expiry returns `DeadlineExceeded`, and active handlers return to zero. Headers establish admission before cancellation. |
| `SlowReaderOtherStreamProgress` | An unread 5 MiB burst has an outstanding write; another stream on that connection completes; canceling the burst releases its handler. Identical buffering/timing is not asserted. |
| `MutualTlsAdmission` | Valid identity succeeds; absent identity returns `Unavailable`; a subsequent valid call proves continued availability. |

Linux executes **46 named checks** across two servers and plaintext/TLS 1.2/TLS 1.3 modes. macOS executes **30** across plaintext/TLS 1.2. These are transport executions outside the MTP test count, not independent xDS scenarios. The CLI verifies membership, uniqueness and positive results. The maintained macOS `transport verify` run passed all 30 checks with matched TLS 1.2; Linux execution remains pending at publication.

## Defaults and configuration differences

The Go root library supplies handlers; its caller configures grpc-go. The [example host](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/internal/example/server.go) also overrides defaults. Example settings are not mandatory root-library behavior.

| Setting | grpc-go 1.84.0 / example | Kestrel / gRPC for .NET | Implication |
|---|---|---|---|
| Server receive message cap | 4 MiB | 4 MiB | Same exercised request boundary. Client receive limits are independent. |
| Server send cap | `math.MaxInt32` | No configured cap | Both allow the exercised 5 MiB response. |
| Streams per connection | Library `math.MaxUint32`; example 1,000,000 | 100 | Different admission defaults. This is not a limit of 100 Envoys; do not raise it without a concrete requirement. |
| Incoming metadata | Legacy 16 MiB; experimental option changes default to 8 KiB | 32 KiB total, 100 headers, 32 KiB field limit | Rejection behavior is not identical; choose hosting limits explicitly. |
| Server keepalive | Library interval 7,200s/timeout 20s; example 30s/5s with client-ping enforcement | Sending disabled by default; timeout 20s when enabled | Intervals, enforcement and connection-age policies are not one-to-one options. |
| Receive windows | Initially 65,535 bytes, adaptive unless configured otherwise | 1 MiB connection / 768 KiB stream | Govern incoming data. Raising Kestrel receive windows does not directly accelerate snapshot downloads. |
| Aggregate HTTP body limit | No aggregate request-body cap in grpc-go | gRPC disables normal Kestrel streaming body-size/data-rate limits | Long-lived streams retain per-message limits; cumulative-body behavior is exercised. |
| Reader/writer ownership | One reader and one writer can progress independently | Same supported ownership; concurrent writes rejected | Await sends and keep one writer. No separate writer service is needed. |

Versioned sources: [grpc-go options](https://github.com/grpc/grpc-go/blob/v1.84.0/server.go), [defaults](https://github.com/grpc/grpc-go/blob/v1.84.0/internal/transport/defaults.go), [Kestrel HTTP/2 limits](https://github.com/dotnet/aspnetcore/blob/a5383385245bdacc20ec19f30e46090a8154d8da/src/Servers/Kestrel/Core/src/Http2Limits.cs), [general limits](https://github.com/dotnet/aspnetcore/blob/a5383385245bdacc20ec19f30e46090a8154d8da/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs), [gRPC call handling](https://github.com/grpc/grpc-dotnet/blob/4c6997a214601422dd66c5c74c1969db749479e9/src/Grpc.AspNetCore.Server/Internal/CallHandlers/ServerCallHandlerBase.cs) and [stream writer](https://github.com/grpc/grpc-dotnet/blob/4c6997a214601422dd66c5c74c1969db749479e9/src/Grpc.AspNetCore.Server/Internal/HttpContextStreamWriter.cs).

TLS/mTLS, interceptors and ASP.NET authentication are available. Compression is optional and disabled here; gzip interoperability is not an executed result. Reflection, health checks and application authorization require separate registration/policy. They are not supplied merely by referencing the server package.

## Performance method

| Response payload | Streams | Connections | Purpose |
|---:|---:|---:|---|
| 1 KiB | 1 | 1 | Small exchange overhead |
| 64 KiB | 1 | 1 | Moderate update |
| 64 KiB | 16 | 1 | Multiplexing |
| 64 KiB | 16 | 16 | Separate clients |
| 1 MiB | 1 | 1 | Large update |

Each mode runs three alternating server trials for every workload. Each worker warms a persistent stream with 100 exchanges, then measures a common two-second interval. Payloads are identical, compression is off, and one Go client handles both servers. Both runtimes receive a processor-count hint capped at four; no affinity/quota is applied. .NET uses server GC. Startup, handshakes and warm-up are excluded.

Latency samples every tenth exchange. Summary rows show median results and the throughput range; raw trials retain counts, elapsed time, p50/p95/p99, server CPU and runtime allocation counters. Linux records **90 trials**, macOS **60**. This is network workload measurement, not a BenchmarkDotNet microbenchmark.

TLS 1.2 uses `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`; Linux additionally measures TLS 1.3 with `TLS_AES_128_GCM_SHA256`. Functional checks assert the actual negotiated version/cipher before measurements. Both servers use the same private CA and ECDSA identities.

The initial task-scoped macOS experiment passed 30 checks and recorded 60 default-negotiation trials. It exposed increased Kestrel TLS allocations and lower TLS throughput, but did not record/pin negotiated TLS parameters. Those numbers motivate controlled Linux measurement; they are not a controlled TLS parity claim. The maintained probe corrects that limitation.

These short loopback trials are diagnostic. Allocation counters have different runtime accounting boundaries and exclude native allocations. Process CPU includes background/runtime work and call cleanup. The driver shares the host; tail samples are limited; there is no controlled offered-load/queue-depth model. Retained heap, idle-connection memory, connection capacity, handshake cost, WAN behavior and production p99/SLOs are not measured. Cache/materialization/callback costs are absent.

## Decision and remaining proof

The stack provides the required transport primitives; no custom HTTP/2 or gRPC implementation is justified. Use the narrow server package, preserve the generated buffer-based marshaller, await one send path and forward cancellation. Avoid adding a full-message `ToByteArray` copy or an unbounded producer queue in adapters.

Read Linux results by workload and TLS mode, including CPU, allocation counters and trial variance. Profile a slow case on the deployment OS before tuning buffers, GC or serialization. For large outgoing snapshots, inspect the client's receive windows before changing Kestrel's incoming windows.

Actual discovery registration, xDS state, application authentication, intermediaries, active-stream graceful shutdown and released-Envoy interoperability remain later implementation/integration evidence. Sustained realistic fan-out and idle-memory capacity require a deployment-scale workload. This probe does not establish publication readiness or complete Go-server parity.
