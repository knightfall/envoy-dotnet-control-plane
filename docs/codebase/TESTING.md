# Testing

Reviewed 24 October 2026. Source paths below are relative to go-control-plane/.

## Upstream reference

- Tests sit beside Go implementations; pkg/integration and pkg/test supply integration/test-support code.
- CI runs make docker_tests. The Makefile declares xDS, ADS, REST, mux and Delta integration variants.
- Cache/server tests total 5,197 source lines including comments; this is a static size measure.

## Proposed .NET verification

- Generate/restore, pack and consume the chosen bindings first; include HCM/router/TLS Any payloads and rate-limit schemas.
- Compare Go and .NET state transitions, parsed messages, callbacks, statuses and defaults.
- Normalize opaque generated values only with a consistent mapping preserving stale ACK/NACK/version behavior. Deterministic Protobuf is not universally canonical.
- Cover wildcard transitions/options, optional ordered SotW ADS, subscription supersession, Delta removals/initial versions/source nonce behavior, TTL encoding/SotW heartbeats, fetch limitations, and cancellation/cleanup.
- Include client ACK/NACK state, gRPC unary Fetch and HTTP JSON/304 behavior.
- Gate release with actual Envoy update/NACK/reconnect/TTL cases and pinned image digests.
- [TODO] Select the .NET test framework and CI matrix during repository setup.
- Use the [task acceptance criteria](../plan/IMPLEMENTATION-PLAN.md) and [named verification cases](../plan/TRACEABILITY.md). T03 provides a bounded binding/metadata and Go PGV comparison proof at G1. T26/GV expands to the full claimed catalog, applicable evaluator conformance, structured failures, clean consumers, and per-update rule coverage. Unsupported constraints cannot pass silently.

No runtime tests, generation or package builds were executed during this planning review.

## Evidence

- Makefile; .github/workflows/ci.yaml
- pkg/cache/v3/simple_test.go; linear_test.go; delta_test.go
- pkg/server/v3/server_test.go; delta_test.go; gateway_test.go
- pkg/client/sotw/v3/client_test.go; pkg/integration/ttl_integration_test.go
- [Protobuf serialization](https://protobuf.dev/programming-guides/serialization-not-canonical/)
