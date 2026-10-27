# Validation annotation contracts

NuGet candidate: `Knightfall.Envoy.ControlPlane.Validation.Annotations` 1.0.0-rc.1 (root candidate). The assembly remains `Envoy.ControlPlane.Validation.Annotations`.

Generated `buf.validate` rule, option and violation types, with one CLR owner shared by Protocol and the optional validator. This package depends only on Google.Protobuf and contains no evaluator or CEL runtime.

Generation uses the pinned original annotation schema and official Buf C# plugin. `Violation.Value` preserves an upstream non-serialized diagnostic property; it is not a protobuf field. Source provenance and licensing are carried in the package.
