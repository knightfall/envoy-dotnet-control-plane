# Verifying a published package

Use this guide to find the evidence for the specific package and version you
received. Release assets and publishing workflows can change between releases;
check the matching release's notes and evidence before applying its verification
procedure. A package's embedded metadata is a locator, not proof of its origin.

## Find the matching release

1. Identify the package ID and version in the package's `.nuspec` file. A
   `.nupkg` is a ZIP archive. Its nuspec also records `repository.commit`.
2. If present, read `security/provenance.json` in the package for its release,
   repository, workflow, source ref and attestation locations. Confirm that its
   version matches the nuspec. Treat these values as untrusted until verified.
3. Open the matching release from the project's [GitHub releases](https://github.com/knightfall/envoy-dotnet-control-plane/releases)
   and check its tag, package ID, version, source commit and stated publishing
   workflow against the repository's release evidence. Establish the expected
   source commit independently of the package before using it as a verifier
   constraint. Obtain the original upload artifact and applicable signed
   provenance or bundle from that release. If matching evidence is missing or
   conflicts, do not treat the package as verified.

## Verify the original upload

Use the verification instructions and signer identity for the matching release.
Verify the original upload's bytes against its signed provenance, including the
expected repository, publishing workflow, source ref and independently checked
source commit. Where the release supplies a bundle, use it if required by that
release's instructions; availability through GitHub's attestation lookup can
vary. A successful verification establishes the claims checked by the tool; it
does not establish that the package is safe or suitable for a particular use.

For the first release, the following command was verified against the original
Core package downloaded from [v1.0.0-rc.1](https://github.com/knightfall/envoy-dotnet-control-plane/releases/tag/v1.0.0-rc.1):

```sh
gh attestation verify Knightfall.Envoy.ControlPlane.1.0.0-rc.1.nupkg --repo knightfall/envoy-dotnet-control-plane --signer-workflow knightfall/envoy-dotnet-control-plane/.github/workflows/publish.yaml --source-ref refs/heads/main --source-digest 000438a9c4d0bc0b58a5ec7bc55af10f5f9c0ccd --deny-self-hosted-runners
```

The same constraints were verified for the other four original package assets
in that release, using their respective filenames. These values are specific to
that release; do not copy its source digest or assume its workflow identity for
another version. Its release also includes signed provenance and SBOM bundles,
SBOMs and an evidence archive; consult the matching release for its own assets.

## Verify a NuGet download

For the first release, NuGet repository signing adds `.signature.p7s` after
upload, so the downloaded `.nupkg` has different archive bytes from the
attested original. Verify the downloaded package's signature:

```sh
dotnet nuget verify <downloaded-package.nupkg> --all
```

Then compare its contents with the verified original from the same release.
The first release's [comparison script](https://github.com/knightfall/envoy-dotnet-control-plane/blob/v1.0.0-rc.1/.github/release/compare-packages.py)
and its [helper](https://github.com/knightfall/envoy-dotnet-control-plane/blob/v1.0.0-rc.1/.github/release/package_payload.py)
compare ZIP entries while excluding `.signature.p7s`; they reject duplicate
names among the remaining entries. Review and obtain both scripts from the
trusted matching release source before running them from their directory:

```sh
python3 compare-packages.py <verified-original.nupkg> <downloaded-package.nupkg>
```

The publishing workflow used this comparison and signature verification for its
NuGet consumer. Signature verification alone does not prove that the downloaded
payload matches the attested original, and comparing payloads does not replace
signature or provenance verification. Check later release instructions for any
changes to signing or comparison.

The [publishing record](PUBLISHING.md) describes the first release's controls
and observed publication evidence. It is not a guarantee for later releases.
