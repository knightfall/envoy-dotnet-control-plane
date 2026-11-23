# Contributing

Thank you for contributing. Open an issue to discuss a substantial change or an unclear compatibility decision before implementing it. See [SECURITY.md](SECURITY.md) to report vulnerabilities privately.

## Set up

Use the exact SDK in [global.json](global.json). A fresh checkout needs generated bindings before building projects that reference Protocol. Choose a writable source cache outside the checkout and follow the [tool guide](tools/README.md) to generate bindings. The guide describes the pinned source and tool downloads and network requirements.

## Make and verify a change

Keep changes focused and follow the conventions and compatibility boundaries in [AGENTS.md](AGENTS.md). Add meaningful tests for behavioral changes and update relevant documentation. The [implementation plan](docs/plan/IMPLEMENTATION-PLAN.md) defines intended scope; the [compatibility report](docs/compatibility/root-1.0.md) records tested behavior and limitations.

Run focused checks appropriate to the change. The [test guide](tests/README.md) and [tool guide](tools/README.md) describe prerequisites, repository verification, package-consumer checks and the separate released-Envoy interoperability check. Report the commands and results, including checks that could not run. Generated inputs belong under `obj/` and package output under `bin/`.

## Submit a pull request

Describe the problem, the change and the evidence reviewers need to assess it. Link the relevant issue when one exists. The repository uses pull requests and required checks; maintainers review results and address actionable feedback before merging. See the [publishing process](docs/releases/PUBLISHING.md) for release-specific controls.
