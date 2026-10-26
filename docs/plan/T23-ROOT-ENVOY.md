# T23 — Released Envoy Delta and TTL interoperability

Completes the remaining harness scope of [issue #29](https://github.com/knightfall/envoy-dotnet-control-plane/issues/29), retaining the [twenty SotW checks](T23-PREVIEW-ENVOY.md). The maintained command is unchanged: repository CLI, envoy verify. It runs the exact released proxy binary beside the actual .NET libraries in the pinned SDK container, with external networking disabled. No library behavior, package dependency, schema, generated fingerprint or image pin changes.

## Advertised matrix and evidence

The selected release is Envoy 1.40.1, source 86af01fdec40c31d025c495a4aa303c4126b2fd0. [The image lock](../../tests/resources/envoy-images.lock.json) retains independent immutable Linux amd64/arm64 image digests and SDK 10.0.400 digests. CI now runs both architectures with separate evidence artifacts; observed exact-head CI results belong to the implementing PR. Local Linux arm64 proof runs through Docker on macOS arm64.

Every architecture runs 35 named checks: ten ordinary SotW/ADS, ten ordered SotW/ADS, nine Delta/ADS and six caller-supplied Delta TTL checks. The report verifier rejects missing, duplicate, reordered, failed or wrongly labeled reports. Eight additional offline cases independently declare the Delta/TTL inventories and test missing/failed/protocol mismatches; the six existing SotW verifier cases remain.

The exercised graph contains CDS, EDS, RDS and LDS with the existing HCM/router example. Other supported root methods retain their generated-client HTTP/2 and Go differential evidence; this scenario does not claim complete workloads for SDS, SRDS, ECDS, RTDS, VHDS, LEDS or rate-limit discovery. A successful connection does not establish support for every field in the newer schema catalog, TLS deployment, every extension, or another Envoy release.

## Delta updates, removal and reconnect

DeltaEnvoyScenario and DeltaProbeCallbacks use the existing SnapshotCache, DeltaServer, ASP.NET adapters and official generated Envoy stubs. The bootstrap is the attributed upstream ADS fixture with DELTA_GRPC selected. No Delta selection, hashing, nonce or reconnect algorithm is copied into the probe.

1. Envoy requests and ACKs all four families and routes application traffic to backend A.
2. An EDS payload update moves traffic to B. The admin endpoint dump must contain the selected backend port.
3. An RDS update changes the rewritten path; both traffic and the admin route object must match.
4. Removing the service cluster produces the exact Delta tombstone, a successful ACK, no active admin cluster and HTTP 503. The deliberately partial snapshot bypasses only the example's opt-in consistency assertion; SetSnapshot remains unvalidated as designed.
5. Re-adding CDS restores the active cluster and traffic.
6. Stopping the management host releases all stream watches while the same Envoy process keeps routing with its accepted graph.
7. On reconnect, all four types must send an empty response nonce and initial_resource_versions equal to the accepted per-resource hashes. This is actual proxy behavior, not an invented replay request. Unchanged named resources need not trigger a new response.
8. A post-reconnect EDS change moves traffic back to A and must be ACKed and visible in admin state.
9. Admin shutdown exits Envoy successfully; all connections close, watches reach zero and no request carries a NACK.

Callback observations preserve actual node/type/subscribe/unsubscribe/initial-version/nonce/error data, resource hashes and removal names. Reports retain the full response history, not only the final response per type. Versions and wire associations remain distinct from application traffic proof.

## TTL scope and observed SotW limitation

The raw pinned Go/.NET SnapshotCache intentionally omits Delta TTL; neither adds automatic Delta heartbeats. TtlDeltaWatcher is a non-packaged, test-only producer using the existing PassthroughDeltaResponse contract. It decorates the initial CDS response with a three-second TTL and publishes payload-free envelopes every 500 ms through the ordinary channel writer. It copies returned-version metadata at watch creation, owns one timer/task, awaits cancellation and forwards registration disposal to the underlying cache. It contains no configuration selection, serialization, hashing, server or client state machine. All other resource families use SnapshotCache directly.

TtlEnvoyScenario proves initial traffic, then successful requests for more than twice the original TTL with exactly one full CDS payload and at least three payload-free refreshes. Name, collection version, resource version and TTL must remain unchanged. After the caller stops and awaits the timer, traffic becomes HTTP 503, the service cluster disappears from the admin dump, and cluster_manager.cluster_removed increases. No full configuration is resent during that interval. The original .NET snapshot and pending watches remain present. A deliberately changed CDS payload repairs the proxy; a collection-version-only change would correctly be suppressed by the Delta cache's resource hashes. Shutdown ends the publisher and all watches.

The initial investigation also ran source-equivalent SotW CDS heartbeats against the default Envoy 1.40.1 mux. The first payload-free response removed the cluster instead of preserving it until TTL expiry. The released source explains the boundary: [GrpcMuxImpl filters heartbeat resources before processing the full state](https://github.com/envoyproxy/envoy/blob/86af01fdec40c31d025c495a4aa303c4126b2fd0/source/extensions/config_subscription/grpc/grpc_mux_impl.cc#L426), while [CDS removes omitted clusters](https://github.com/envoyproxy/envoy/blob/86af01fdec40c31d025c495a4aa303c4126b2fd0/source/common/upstream/cds_api_impl.cc#L51). The legacy implementation also skips TTL updates for those filtered heartbeats. The release's unified-mux runtime flag is disabled by default; no alternative mux remedy is claimed here. RDS/EDS removals have their own semantics and cannot be substituted for a CDS expiry proof.

Consequently **source-equivalent SotW CDS heartbeat interoperability is not supported by the advertised default proxy configuration**. Core retains its Go-matching implementation and the native/source T17 proof; this run does not claim that the built-in SotW scheduler passed V33. V33 here covers explicit caller-supplied Delta TTL envelopes. Consumers requiring SotW TTL should validate their exact proxy version/resource/mux rather than treating source parity as proxy acceptance. This limitation is exposed rather than hidden by a successful initial connection.

## Retained artifacts and verification

Protocol's obj/upstream/results/envoy contains exact library revision/dirty state, behavior/schema pins, full image lock, extracted binary SHA-256/version, generated bootstrap files, per-scenario JSON, actual request/response observations, typed/structured admin dumps, complete statistics and stdout/stderr. Container mounts, isolated networking and ownership cleanup remain as in the preview harness. Timeouts fail; no failed proxy run can be counted as passing.

Observed locally: the final pinned Linux arm64 Docker run passes all 35 checks, with no NACKs outside the deliberately rejected SotW route. Full macOS arm64 repository verify passes 482 solution cases (Core 264, Hosting 109, Protocol 14, optional Validation 18, tooling/provenance 77), zero failed/skipped, zero compiler/Roslynator warnings/errors, all Go comparisons/negative checks, five package/symbol pairs and fresh consumers (14/18/264/109) plus both executable package graphs. Three existing non-packable project notices remain. Exact-head Linux amd64/arm64 CI and native PR review remain pending and are recorded separately in the implementing PR. Behavior commit 67c5345367582741334e34ffc66a561b0ce32946, schema commit 82a4424f760b6ba3ffc9f2911389d061656f35c8, SDK and Go pins are unchanged. Root package proof is separate from released-proxy proof, and T26/GV remains separate. The manual performance baseline remains manual; no NuGet package is published.
