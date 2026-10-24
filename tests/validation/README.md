# Validation research and Go comparison

`GoOracle/` is part of the maintained T03 verification path. It decodes the messages exported by the current .NET tests and compares their expected outcomes with the pinned generated Go PGV validators.

`Celly.Proof/`, `ProtoValidate.Proof/`, `Support/`, and `cases.json` preserve the earlier published-package assessment. They are historical research, excluded from the maintained build and CI entry points. Their recorded observations do not describe the selected source-built implementation. Celly cleanup remains deferred.

The earlier orchestration helpers, `scripts/validation/ValidationAssessment.cs` and `ValidationMigration.cs`, are also retained historical source. Their original command names are no longer exposed by `scripts/Validation.cs`.

Use [the current validation tests](../Envoy.ControlPlane.Validation.Tests/) and [T03 evidence](../../schemas/VALIDATION.md) for supported commands, results and remaining acceptance gates.
