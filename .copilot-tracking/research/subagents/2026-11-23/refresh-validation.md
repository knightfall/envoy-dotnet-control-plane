# Refreshed documentation audit validation

## Questions

- Is Git operational, and does the tracked Markdown inventory match the prior 75-file filesystem inventory?
- Which Markdown lint and link validation tools are available, and what non-mutating checks are practical?
- Can the published-release attestation question be verified without modifying external systems?

## Findings

- Inside the sandbox, `git --version` and `git status --short --branch` both
  failed with `xcrun: error: invalid active developer path`, and the Homebrew
  binary directory appeared empty. The parent confirmed Git works outside the
  sandbox, with HEAD `895c31c499daa914bc5e52831833f8e11aaee0ce` and no
  product changes. Independently, read-only parsing of the v2 Git index with
  Ruby found 575 entries and 76 Markdown paths including `AGENTS.md`. Excluding
  that agent guidance and the prior audit's excluded directories produced 75
  indexed paths and 75 present paths, with no difference. Index membership is
  not itself a clean-working-tree or HEAD check.
- Outside the sandbox, `gh`, `node`, and `npm` are available. None of
  `markdownlint`, `markdownlint-cli2`, `markdown-link-check`, `lychee`, or
  `rumdl` was on PATH. `npm list -g --depth=0` listed only
  `@astudioplus/codegraph-mcp@0.20.1`; no configured Markdown lint or link
  check was found in the repository. A lightweight Ruby scan of 75 scoped
  Markdown files identified 73 opening fences and zero unclosed fences. This
  does not cover full Markdown syntax, rendering, or links. No tools installed.
- `gh release view v1.0.0-rc.1 --repo knightfall/envoy-dotnet-control-plane
  --json tagName,isDraft,isPrerelease,assets` found a published prerelease with
  five original `.nupkg` files and a provenance bundle. GitHub CLI 2.100.0 was
  used for the following read-only release verification. Downloads were kept
  in a temporary directory, not the checkout:

  ```sh
  gh release download v1.0.0-rc.1 --repo knightfall/envoy-dotnet-control-plane --dir <temporary-directory> --pattern Knightfall.Envoy.ControlPlane.1.0.0-rc.1.nupkg --pattern 'provenance-*.sigstore.json'
  gh release download v1.0.0-rc.1 --repo knightfall/envoy-dotnet-control-plane --dir <temporary-directory> --pattern '*.nupkg' --skip-existing
  ```

  For each of the five package filenames in the package READMEs, the exact
  documented command was run from that directory, with the relevant filename:

  ```sh
  gh attestation verify <package-filename> --repo knightfall/envoy-dotnet-control-plane --signer-workflow knightfall/envoy-dotnet-control-plane/.github/workflows/publish.yaml --source-ref refs/heads/main --source-digest 000438a9c4d0bc0b58a5ec7bc55af10f5f9c0ccd --deny-self-hosted-runners
  ```

  All five commands returned exit 0 without `--bundle`, including Core,
  AspNetCore, Protocol, Validation and Validation.Annotations. The previous
  report's unresolved concern is resolved for this release's five commands;
  this does not prove future releases or explain why repository lookup works
  despite current `.github/workflows/publish.yaml` using
  `create-storage-record: false`. The bundle was downloaded but was not needed
  for these commands. No persistent verification logs were written; command
  output is retained in the tool session, and downloaded files remain in the
  temporary directory. `shasum` could not run inside the sandbox because its
  Perl dynamic library was blocked; no separate digest comparison is claimed.

## Follow-on questions

- For future releases, verify the commands against the corresponding release
  and workflow configuration. Render and perform full lint/link validation when
  the relevant tooling and access are available.

## Clarifying questions

None.
