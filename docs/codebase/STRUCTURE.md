# Structure

Reviewed 24 October 2026. Source paths below are relative to go-control-plane/.

## Upstream reference

- pkg/cache: cache abstractions, snapshots, linear/mux caches, resources, status.
- pkg/server: per-service v3 adapters, SotW/Delta engines, unary fetch, HTTP gateway, subscriptions, options.
- pkg/client/sotw/v3: ADS client with initialization, receive, ACK/NACK and connection-error classification.
- pkg/resource, pkg/conversion, pkg/wellknown, pkg/log: public helpers and logging facade.
- envoy/, contrib/, ratelimit/: generated API modules.
- xdsmatcher/: independent matcher implementation, registry, types, and tests.
- internal/example/, examples/dyplomat/, pkg/test/: examples and test support.
- docs/, scripts/, .github/: documentation and automation.

## Proposed .NET repository

See ../../PARITY-PLAN.md and ../plan/PARITY-MATRIX.md. Recommended packages are Protocol, ControlPlane runtime, and AspNetCore adapters. The matcher is a separate follow-up. [TODO] Freeze actual package IDs before publishing.

## Evidence

- README.md:18-44
- pkg/cache/v3/; pkg/server/v3/; pkg/client/sotw/v3/client.go
- pkg/resource/v3/resource.go; pkg/conversion/struct.go; pkg/wellknown/wellknown.go
- envoy/go.mod; contrib/go.mod; ratelimit/go.mod; xdsmatcher/go.mod
- internal/example/README.md
