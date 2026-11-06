# Validation annotation contracts

NuGet candidate: `Knightfall.Envoy.ControlPlane.Validation.Annotations` 1.0.0-rc.1 (root candidate). The assembly remains `Envoy.ControlPlane.Validation.Annotations`.

Generated `buf.validate` rule, option and violation types, with one CLR owner shared by Protocol and the optional validator. This package depends only on Google.Protobuf and contains no evaluator or CEL runtime.

Generation uses the pinned original annotation schema and official Buf C# plugin. `Violation.Value` preserves an upstream non-serialized diagnostic property; it is not a protobuf field. Source provenance and licensing are carried in the package.

## Supply-chain verification

[Versioned release evidence](https://github.com/knightfall/envoy-dotnet-control-plane/releases/tag/v1.0.0-rc.1) includes build/source-input SBOMs, signed provenance and SBOM bundles, package digests and source pins. [GitHub attestations](https://github.com/knightfall/envoy-dotnet-control-plane/attestations) bind artifacts to the protected publishing workflow. `security/provenance.json` inside this package records the verification locations; its nuspec records the source commit.

Verify the original upload artifact downloaded from the matching GitHub release:

```sh
gh attestation verify Knightfall.Envoy.ControlPlane.Validation.Annotations.1.0.0-rc.1.nupkg --repo knightfall/envoy-dotnet-control-plane --signer-workflow knightfall/envoy-dotnet-control-plane/.github/workflows/publish.yaml --source-ref refs/heads/main --deny-self-hosted-runners
```

NuGet adds its repository signature after upload, changing archive bytes. For a NuGet download, verify that signature with `dotnet nuget verify <package.nupkg> --all` and compare its payload with the attested release artifact using the release verification instructions. The signed bundles are external release evidence; the locator inside the package is not itself an attestation.
