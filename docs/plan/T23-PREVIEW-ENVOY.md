# T23 — Released Envoy preview interoperability

Implements the preview subset of [issue #16](https://github.com/knightfall/envoy-dotnet-control-plane/issues/16). The selected proxy is **Envoy 1.40.1**, released on 23 October 2026 at source commit `86af01fdec40c31d025c495a4aa303c4126b2fd0`. [The official release](https://github.com/envoyproxy/envoy/releases/tag/v1.40.1) and registry manifests were inspected before pinning. Proxy and SDK image indices and per-platform digests are recorded in [envoy-images.lock.json](../../tests/resources/envoy-images.lock.json).

These pins are independent of the Go behavior revision `67c5345367582741334e34ffc66a561b0ce32946` and schema revision `82a4424f760b6ba3ffc9f2911389d061656f35c8`. Neither changes for this proof. No replacement schemas, descriptors, generators or library dependencies are added.

## Maintained command and environment

Generate bindings first, then run from the checkout root with a Linux-container Docker engine:

```sh
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- envoy verify
```

The main CLI stays independent of generated Protocol. It builds the non-packable `Envoy.ControlPlane.EnvoyProbe`, extracts the binary from the exact official Envoy image and runs that binary beside the .NET probe in the exact official SDK 10.0.400 image. The engine selects the locked Linux amd64 or arm64 manifests. The probe, binary and bootstrap mounts are read-only; only the result directory is writable. The container uses the caller's numeric identity so Linux artifacts remain caller-owned. All control-plane, backend and proxy traffic uses loopback inside one container with external networking disabled. No host port or Docker socket is mounted into it.

One real proxy process runs throughout each scenario, including the control-plane outage/reconnect. The management host and two HTTP backends use ordinary Kestrel. The probe references the actual ASP.NET/Core projects and links the exact example resource factory. Fresh messages and independently versioned `ResourceCollection` groups represent endpoint and route updates; messages are never edited after publication. The invalid route is constructed deliberately before publication, with explicit consistency checking but no optional schema evaluator.

The bootstrap adapts the pinned Go source's `sample/bootstrap-ads.yaml`: unused ALS configuration is removed and isolated node/port values are substituted. Its source identity, license and modification notice are retained in [test attribution](../../tests/resources/UPSTREAM-TESTS.md). Generated admin messages and a typed `Any` registry inspect Envoy's accepted route config, rather than searching raw JSON for a string.

## V31 checks

Both default ordinary ADS and optional ordered ADS execute every named check. Checks require actual outcomes; an expired wait, failed process or incomplete report fails the command.

| Check ID | Required observation |
|---|---|
| `bootstrap-and-initial-traffic` | Envoy ACKs CDS/EDS/LDS/RDS and routes `/hello` to backend A. Node identity remains `node`. |
| `endpoint-update` | A new EDS version moves traffic to backend B; the other family versions remain stable. |
| `route-update` | A new RDS version rewrites the path to `/changed/hello`; traffic and typed admin state agree. |
| `nack-association` | A route with an empty domain list is rejected. The NACK retains accepted version `route-update`, names the latest rejected response nonce and contains a diagnostic. Error text is recorded without requiring literal equality. |
| `nack-retains-accepted-config` | Envoy continues serving the accepted route and admin state remains at `route-update`; the application cache still contains `invalid-route`, proving no automatic rollback. |
| `explicit-repair` | The test application publishes a valid replacement. Envoy ACKs `repaired-route` and serves `/repaired/hello`; this is explicit application action. |
| `disconnect-retains-traffic` | The management host stops, its watches end and the same Envoy process keeps serving the accepted configuration. |
| `reconnect-resubscribes` | The management host restarts on the same port. That same proxy reconnects and sends accepted versions with empty old-stream nonces for all four families, then ACKs responses on the new stream. |
| `post-reconnect-update` | A subsequent EDS update moves traffic back to backend A while retaining the accepted route rewrite and admin state. |
| `cleanup` | Envoy's admin shutdown completes with exit code zero, all streams close and no cache watches remain. Owned hosts/processes/containers are cleaned up; no caller cache-disposal policy is added. |

The request callback pauses the deliberate NACK until the application publishes its repair, preventing a repeated-invalid-response loop in the test. This gate is fixture orchestration and does not change the server's NACK behavior. Polling succeeds only on observed traffic/ACK predicates; bounded delays are failure controls, not a timing-based acceptance rule.

The probe references the example project and calls its compiled resource factory through narrow internal access. It does not compile a second copy of that implementation. Clean package-consumer suites retain their focused source links so they can verify candidate packages without referencing application projects.

## Reports and verification

Results live under Protocol's `obj/upstream/results/envoy/`: source/image/environment pins, extracted binary SHA-256, exact library revision and dirty-state flag, binary version, per-mode checks and request/response observations, generated bootstrap, typed-route admin dumps and Envoy stdout/stderr. Inputs are immutable and both image manifests are validated by Docker. Temporary source containers and execution containers are removed; the extracted binary stays outside source control and is deleted after the run.

`EnvoyResultsTests.CompleteSuccessfulReport_AcceptsItsDeclaredMode` verifies both report modes. `IncompleteOrFailedReport_IsRejected` rejects a missing check, duplicate check, failed result and wrong mode. The required ten IDs are declared independently in the verifier. The six cases run in the existing offline suite; the real-proxy command is separate from ordinary unit/package checks so those checks do not require Docker.

Observed locally: the complete command passed **20/20** V31 checks in a Linux arm64 Docker container on macOS arm64. SDK image 10.0.400 runs **.NET 10.0.11** in that container; the report records the actual runtime. Complete native repository verification passes **258** cases, zero failed/skipped and zero compiler/Roslynator warnings/errors, all five package/symbol pairs, fresh Protocol (12), Validation (18), Core (149), Hosting (31) test consumers and executable Core/ASP.NET consumers. Existing Go comparisons and injected-mismatch rejection pass. Solution pack emits three notices for intentionally non-packable example/probe projects.

The [interop workflow](../../.github/workflows/envoy-interop.yml) repeats the same command on Linux amd64 and uploads evidence for the exact PR head. Its observed result and independent GPT-6.1 Sol xhigh review belong to the implementing PR; configuration alone is not a passing result. The existing Linux/macOS/Windows repository checks also build the probe and run the report-verifier cases.

Claim only platforms with observed successful runs; the implementing PR records Linux amd64 CI separately from local arm64. This scenario covers one released proxy version, plaintext SotW/ADS and the four-family example graph. Other advertised methods retain T11's generated-client HTTP/2 evidence; this scenario does not exercise their complete Envoy workloads. Delta, TTL, other proxy releases, full schema-feature compatibility, deployment capacity and optional-validator conformance remain separate. No performance result, NuGet publication or broader parity claim is inferred from these checks.
