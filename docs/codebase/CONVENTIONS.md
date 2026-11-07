# Conventions

This is a historical upstream-source review. Its proposed .NET directions and TODOs reflect the 24 October 2026 planning stage. For the implemented surface and evidence, see the [root compatibility report](../compatibility/root-1.0.md), [public API contracts](../public-api.md) and [validation conformance report](../plan/T26-VALIDATION-CONFORMANCE.md).

Reviewed 24 October 2026. Source paths below are relative to go-control-plane/.

## Upstream reference

- .golangci.yml configures linting; Makefile exposes build/test/integration/generation-maintenance targets.
- Public server callbacks run in the calling operation's path; open/request errors reject that operation.
- Default ordered ADS is false. Legacy wildcard semantics are active unless deactivated globally or for selected types.

## Proposed .NET conventions

- Use .NET 10, nullable reference types, CancellationToken, Task/ValueTask and ILogger.
- Keep Protocol, runtime ControlPlane, and AspNetCore responsibilities separate. The optional validation package owns evaluator dependencies; root packages do not reference it. It references the shared annotations package, rather than Protocol.
- Preserve behavior/defaults while recording .NET API departures; avoid copying Go channels, functional options, or logger APIs mechanically.
- Pin schema inputs and generator versions; keep maintained generation/compatibility tooling in tools/.
- [TODO] Choose package IDs and freeze public API baselines after the binding proof.

## Evidence

- .golangci.yml; Makefile
- pkg/server/v3/server.go:64-84
- pkg/server/sotw/v3/server.go:38-50,63-90
- pkg/server/config/config.go:20-38
- ci/sync_envoy.sh:23-49
