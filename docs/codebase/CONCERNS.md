# Concerns

Reviewed 24 October 2026. Source paths below are relative to go-control-plane/.

## Scope and maintenance risks

- Behavioral scope includes root-library fetch/client/helper APIs; generated Envoy service descriptors do not imply implementations for every service.
- envoy/ and contrib/ contain 1,488 *.pb.go and 717 validation outputs at this pin; ratelimit/ adds two protobuf outputs. Generation variants must not be counted as equivalent handwritten implementation work.
- Common extension messages inside Any require broader bindings than the discovery envelope's import closure. Avoid duplicate message ownership across generated assemblies.
- The original rate-limit schema revision and complete imported-source pins require resolution.
- Optional schema validation requires original options from the first binding release and T03 feasibility before G1. Protovalidate candidates need PGV equivalence and single descriptor ownership proof. Full T26/GV is separate from root-runtime release. Every schema update reports rule changes and unsupported constraints; no publication pipeline is added. See the [validation plan](../architecture/VALIDATION.md).
- The source still implements HTTP fetch despite an undated “soon deprecated” docs note. Recommended 1.0 includes it and unary gRPC fetch.
- ADS coordination is not a global transaction; preserve ordered/wildcard defaults and per-stream semantics.
- Delta's nonce/returned-resource tracking differs from SotW, and the automatic SnapshotCache heartbeat timer serves SotW watches. Avoid adding unproven common-mode behavior.
- Snapshot.Consistent is optional and validates the source's endpoint/route references. Preserve the implemented scope.
- Deterministic serialized bytes and resource hashes need not match across Go/C# runtimes; test version relationships and explicitly document literal-hash compatibility.
- Latest mirrored schema support does not establish compatibility with every released Envoy version.
- xdsmatcher requires separate match-tree/registry/legacy-format coverage.

## Evidence

- README.md:18-44,64-74; docs/server.md:3-8
- pkg/server/v3/server.go:44-62,226-357; gateway.go:38-98
- pkg/client/sotw/v3/client.go; pkg/server/config/config.go:20-38
- pkg/cache/v3/snapshot.go:83-121; simple.go:264-286; resource.go:145-147,300-305
- ci/sync_envoy.sh:23-49; envoy/COMMIT; xdsmatcher/pkg/matcher/matcher.go
- [PGV generators](https://github.com/bufbuild/protoc-gen-validate#parameters)
- [Protobuf serialization](https://protobuf.dev/programming-guides/serialization-not-canonical/)
