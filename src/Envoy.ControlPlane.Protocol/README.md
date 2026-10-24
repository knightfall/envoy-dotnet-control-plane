# Envoy.ControlPlane.Protocol candidate

T02 candidate net10.0 bindings generated through Buf CLI and pinned official
C#/gRPC plugins from the repository's unchanged upstream catalog.
The provider remains provisional until T03 proves validation feasibility at G1.

The assembly owns the selected Envoy, xDS/UDPA, ratelimit, CEL, Google API/RPC,
PGV, OpenTelemetry and Prometheus messages and annotation definitions.
Google.Protobuf owns well-known types and descriptor option messages.
Do not combine it with another assembly defining the same protobuf types,
including Google.Api.CommonProtos or another Envoy binding catalog.

Namespaces follow the original `csharp_namespace` options or protoc defaults.
gRPC client and server contracts use Grpc.Core.Api; consumers choose their own
channel and hosting transport. No cache, server implementation or validation
evaluator is included. Original PGV options are metadata and are not executed.

The embedded `Envoy.ControlPlane.Protocol.catalog.pb` resource records original
source descriptors. The package's `schemas/` directory contains source and tool
locks, catalog, annotation inventory and third-party notices. Three generated
C# naming corrections allow the complete selected catalog to compile without
changing protobuf identity. See the repository's `schemas/BINDINGS.md` for those
exact corrections, runtime-retention behavior and descriptor normalization.
