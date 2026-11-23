# Knightfall.Envoy.ControlPlane.Protocol

The net10.0 bindings are generated through Buf CLI and pinned official
C#/gRPC plugins from the original upstream catalog. T03 adds derived
Protovalidate options while preserving all original PGV options. The binding
and source-provider arrangement passed bounded G1 proof, with later T25 package verification. The NuGet ID has the Knightfall prefix; the assembly remains Envoy.ControlPlane.Protocol.

The assembly owns the selected Envoy, xDS/UDPA, ratelimit, CEL, Google API/RPC,
PGV, OpenTelemetry and Prometheus messages and annotation definitions.
Google.Protobuf owns well-known types and descriptor option messages.
Do not combine it with another assembly defining the same protobuf types,
including Google.Api.CommonProtos or another Envoy binding catalog.

Namespaces follow the original `csharp_namespace` options or protoc defaults.
gRPC client and server contracts use Grpc.Core.Api; consumers choose their own
channel and hosting transport. No cache, server implementation or validation
evaluator is included. The annotation-only dependency owns buf.validate types;
Protocol has no CEL/evaluator dependency. The separately installed optional
validation package evaluates derived options through ProtoValidate source.

The embedded `Envoy.ControlPlane.Protocol.catalog.pb` resource records original
source descriptors. The package's `schemas/` directory contains source and tool
locks, catalog, annotation inventory and third-party notices. Three generated
C# naming corrections allow the complete selected catalog to compile without
changing protobuf identity. See the [binding report](https://github.com/knightfall/envoy-dotnet-control-plane/blob/main/schemas/BINDINGS.md) for those
exact corrections, runtime-retention behavior and descriptor normalization.

The separate `validation.catalog.pb` embedded resource records derived descriptors. Original source descriptors remain in `catalog.pb`; validation/source locks and original licenses identify the transformation.

## Supply-chain verification

Find the matching package and its release evidence on [GitHub Releases](https://github.com/knightfall/envoy-dotnet-control-plane/releases). The package's `security/provenance.json` identifies verification locations; it is not itself an attestation. Follow the [package verification guide](https://github.com/knightfall/envoy-dotnet-control-plane/blob/main/docs/releases/VERIFYING.md) to verify the release artifact and compare it with a NuGet download.
