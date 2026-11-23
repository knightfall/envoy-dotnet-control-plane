# Release documentation safeguards research

## Questions

- Where are release version, source commit and package README references verified?
- Where should local documentation target and anchor verification run before package publication?
- What existing mechanisms, platform constraints and test approaches apply?

## Findings

### Release identities and immutable artifacts

- `src/Directory.Build.props:9-19` sets the current package version, derives package release notes from `$(Version)`, and embeds `docs/releases/provenance.json` into each package. `.github/release/policy.json` separately declares expected version, repository and five package identities.
- `tools/Envoy.ControlPlane.Tools/Release/ReleaseCandidate.cs:18-40,93-105` requires a clean checkout and matching current verification snapshot; checks each package and symbol nuspec identity, version, repository and source commit against policy and the checkout HEAD; and copies the artifacts to the candidate.
- `.github/release/verify.py:24-62` independently checks the manifest, digests, package set and nuspec identity, source commit and repository. It checks locator version, workflow and source ref but currently does not check locator repository/release URL or package README versioned references. `tools/Envoy.ControlPlane.Tools/Repository/PackageContentsVerification.cs:23,50` checks package readme presence and locator bytes against the source file.
- `.github/workflows/publish.yaml:49-54,93-98,134-135` runs repository verification and candidate preparation, verifies the actual candidate and artifact transport before promotion, and runs the independent verifier before signing and publication. `.github/release/preflight.ps1:3-6` checks that the protected lightweight release tag `v<policy version>` resolves to exact `GITHUB_SHA`.
- The package README `gh attestation verify` examples identify `1.0.0-rc.1`, include individual package filenames, `--source-ref refs/heads/main`, signer workflow and `--source-digest 000438a9c4d0bc0b58a5ec7bc55af10f5f9c0ccd` (for example `src/Envoy.ControlPlane/README.md:128-133`). That digest is historical, belonging to the published release, not a new documentation candidate's HEAD. A tracked README cannot directly contain the hash of the commit containing itself. Checking the old example against every candidate HEAD would therefore reject correct historical examples. Either replace such fixed commands with a version-neutral guide explaining how to get and independently validate the actual package nuspec commit and parameterize the command, or generate the embedded README at pack time from the candidate commit and check the final package bytes. The protected release pipeline assumes source ref `refs/heads/main` and workflow `.github/workflows/publish.yaml`; `verify.py` checks these in the locator.
- To guard future releases without needing an existing tag at PR time, derive expected release URL from policy repository and version, check it and the locator repository, and validate any version-specific links and package identities in the README embedded in each actual nupkg. `verify.py` runs with the trusted expected commit independently validated against the nuspec; source-commit validation must apply to any explicit current-release digest, not an example explicitly labeled historical. Inspect the nuspec releaseNotes too if this is part of the claimed guard. Test wrong release version/repository, missing required reference, wrong package ID/version, stale commit/ref, and valid examples.

### Documentation links

- No existing dedicated Markdown link checker or Markdown parser dependency was found in repository tools, tests, central package management or workflow configuration. `tools/Envoy.ControlPlane.Tools/Repository/RepositoryVerification.cs:10-20,52-63` runs the full verification, tests, package inspection and consumers; `publish.yaml:49-52` invokes it before release preparation. Integrating a focused link verifier there is a cross-platform .NET option. Another option is a scoped, standard-library Python check called in the release build, with unit tests included in `.github/workflows/workflow-security.yml:23-24`. Python is already used by the publishing verification on Ubuntu; adding it to other platform jobs requires checking tool availability separately.
- Map absolute links under `https://github.com/<policy repository>/blob/main/` in package READMEs to candidate-tree local paths, and resolve relative links against the containing file. Check target existence and fragments; preserve percent decoding, case sensitivity and GitHub-style duplicate heading identifiers. The check proves the target exists in the candidate tree; it cannot prove a new page exists on live `main` before the branch is merged. A tagged URL requires checking that immutable revision, not silently treating it as current checkout.
- Explicitly declare parser coverage. A regex-only scanner is insufficient to claim all Markdown, particularly reference-style links, fenced code, inline HTML, escaped delimiters, explicit anchors and duplicate headings. Use a pinned parser if full coverage is required, or fail clearly on unsupported syntax and scope claims to supported links. Tests should cover valid and missing paths and fragments, relative and `blob/main` links, encoded paths, repeated headings, code fences, ignored external links and README/package content equivalence. Run the check on packaged bytes or prove they equal the checked source.

## Recommended next work

- [ ] Implement and test the chosen candidate-package version/commit reference safeguards, taking the self-referential commit problem into account.
- [ ] Add a documented, tested link check in the release candidate path and distinguish candidate-tree checks from live-main or tagged URL verification.
- [ ] Verify the current changes against the actual packed artifacts and required platform/CI entry points; research alone did not run those gates.

## Clarifying questions

None required. The user's approval covers the proposed guards; choices can follow repository conventions and the actual documentation content.
