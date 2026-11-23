# Documentation discoverability research

## Questions

- Identify missing, inaccessible, inconsistent, or broken documentation, links, onboarding, and discoverability using local evidence.
- Compare documentation with source, examples, tools, tests, and workflows.

## Findings

1. The current-checkout reproduction instructions in docs/preview/README.md:9-15 require Go 1.26.4, but schemas/validation/sources.lock.json:302-303 now declares 68 corpus cases and Go 1.26.10, and tools/Envoy.ControlPlane.Tools/Validation/ValidationVerification.cs:40-42 rejects an alternate Go version. Following the guide's Go version while running its repository verification fails. docs/plan/T04-BUILD-FOUNDATION.md:22 has the same current reproduction issue. docs/compatibility/root-1.0.md:24 calls 1.26.4 the comparison Go version without clarifying whether it refers to release evidence or current reproducibility. Follow the lock for current instructions and label historical evidence without changing its recorded version.
2. schemas/VALIDATION.md:21-27 says the current linked validation source lock specifies 33 cases and Go 1.26.4. It instead specifies 68 and 1.26.10. The historical 33-case evidence later in the document should retain its historical values.
3. Published package readmes refer readers to detailed evidence and release verification instructions without a direct destination. src/Envoy.ControlPlane.Validation/README.md:15 refers to the T26 report, src/Envoy.ControlPlane.Protocol/README.md:25-26 uses a bare repository path, and the final paragraph in each of the five src package readmes refers to release verification instructions without linking the corresponding docs/releases/PUBLISHING.md:31 procedure. These files are packed as package READMEs; absolute, ideally versioned, GitHub URLs would help readers navigating from NuGet. This is a navigation gap, not an asserted broken Markdown link.

## Coverage and limitations

Fully read all 19 repository Markdown files under schemas/, src/, scripts/, tools/, tests/, and examples/ excluding generated bin/obj and tracking files: schemas/BINDINGS.md, schemas/NOTICES.md, schemas/PROVENANCE.md, schemas/VALIDATION.md, schemas/validation/assessment-2026-10-25.md; five src package READMEs; scripts/README.md; tools/README.md; tests/README.md, tests/consumers/README.md, tests/resources/README.md, tests/resources/UPSTREAM-TESTS.md, tests/validation/README.md, tests/Envoy.ControlPlane.Validation.Tests/Fixtures/README.md; examples/Envoy.ControlPlane.Example/README.md. Also examined root README.md, PARITY-PLAN.md, SECURITY.md and selected docs/ reports for cross-checking. Other agents own the full docs/ and root review.

A Ruby scan checked inline relative file destinations across 76 repository Markdown files including AGENTS.md; none were missing. A lightweight heading-anchor scan found no unmatched local Markdown anchors with its simplified slug rules. These checks did not cover reference-style links, HTML links, external URLs, GitHub rendering behavior, command execution or every factual assertion. No tests, network retrieval or examples were run. Initial git command failed because xcrun reports an invalid active developer path; this is an environment limitation, not evidence of repository state.

## Follow-on research

- If authorized, check external links and published NuGet rendering online.
- After fixes are agreed, exercise amended instructions in a fresh checkout with the locked Go version and verify package README rendering.

## Clarifying questions

None needed for these findings.

## Follow-up: non-task planning documents

Read docs/plan/IMPLEMENTATION-PLAN.md, docs/plan/PARITY-MATRIX.md, docs/plan/TRACEABILITY.md, docs/plan/DESIGN-AUDIT-2026-10-24.md, docs/plan/REVIEW-2026-10-24.md, docs/plan/GRPC-KESTREL-VALIDATION.md and docs/plan/archive/PARITY-PLAN-2026-10-12.md in full. Historical status and observed results are generally identified as such. A low-priority clarification would annotate the historical claim in docs/plan/archive/PARITY-PLAN-2026-10-12.md:26 that ADS updates related collections atomically, linking to the corrected caveat in docs/plan/PARITY-MATRIX.md. Preserve the original draft text and explicitly label the correction; the file-level historical banner already links current evidence. No additional substantive issue was found in these seven documents.
