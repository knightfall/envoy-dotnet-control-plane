# Validation annotation contracts

NuGet package: `Knightfall.Envoy.ControlPlane.Validation.Annotations` (annotation contracts). The assembly remains `Envoy.ControlPlane.Validation.Annotations`.

Generated `buf.validate` rule, option and violation types, with one CLR owner shared by Protocol and the optional validator. This package depends only on Google.Protobuf and contains no evaluator or CEL runtime.

Generation uses the pinned original annotation schema and official Buf C# plugin. `Violation.Value` preserves an upstream non-serialized diagnostic property; it is not a protobuf field. Source provenance and licensing are carried in the package.

## Supply-chain verification

Find the matching package and its release evidence on [GitHub Releases](https://github.com/knightfall/envoy-dotnet-control-plane/releases). The package's `security/provenance.json` identifies verification locations; it is not itself an attestation. Follow the [package verification guide](https://github.com/knightfall/envoy-dotnet-control-plane/blob/main/docs/releases/VERIFYING.md) to verify the release artifact and compare it with a NuGet download.
