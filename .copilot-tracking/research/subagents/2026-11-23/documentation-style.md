# Documentation style, structure and formatting audit

## Research questions and scope

Identify substantive improvements in writing style, navigation, structure, and formatting across repository Markdown and contributor-facing root documents. Preserve historical records; compare claims with the reader's stated context where practical.

## Inventory and coverage

Inventory: 75 Markdown files outside agent configuration/tracking, including 53 in docs/, 14 across scripts, tools, src, tests and examples, five in schemas/, and three root Markdown documents. LICENSE is a separate root text file. File inventory and headings/patterns were scanned. Complete prose was read for README.md, SECURITY.md, PARITY-PLAN.md, scripts/README.md, docs/usage.md, docs/public-api.md, all seven docs/codebase/*.md files, all three docs/architecture/*.md files, docs/preview/README.md, all three docs/releases/*.md files, and docs/compatibility/root-1.0.md: 21 Markdown documents. The remaining 54 received partial inspection. LICENSE was not assessed. No assertion of comprehensive line-by-line review or example execution is made.

Markdown lint tools and Node were absent from PATH; no Markdown lint was run. No example or external links were executed/checked during this investigation.

Follow-up assignment: fully read all 13 existing T18–T26 task reports: T18-FETCH-GATEWAY, T19-SOTW-ADS-CLIENT, both T20 reports, both T21 reports, both T22 reports, both T23 reports, both T24 reports and T26-VALIDATION-CONFORMANCE. There is no separate T25 report under docs/plan; docs/releases/root-1.0.md was read earlier. This increases full-read coverage to 34 of the 75 Markdown documents; the remaining 41 were partially inspected.

## Findings

1. Medium, navigation and historical framing: README.md:139 labels docs/architecture/ARCHITECTURE.md and docs/architecture/DECISIONS.md as general architecture and decisions, although these are historical planning records. The former explicitly dates itself to 26 October, identifies sketches as noncurrent and retains an unresolved-decisions table (docs/architecture/ARCHITECTURE.md:3,85-87,288-301). The decision register says its current-looking status columns preserve historical recommendations and proofs (docs/architecture/DECISIONS.md:3,9-20). Make historical status visible in the entry point and provide a concise current architecture route; retain original evidence without rewriting history.
2. Medium, verification guide usability: tests/README.md:3 notes the task sections are historical, yet the rest interleaves current commands and present-tense incremental counts. For example, tests/README.md:95-101 describes 92/102/123 cases and older boundaries, while later sections discuss root and live integration. Move historical chronology to dated task reports or clearly group it and prioritize the current suite map, prerequisites and entry points. This is a navigation/interpretation issue, not evidence of inaccurate historical test results.
3. Low, incomplete custom-family onboarding: docs/usage.md:131-145 tells readers to generate Example.V1.Widget but does not give generation steps, project configuration or a runnable sample link. The subsequent code depends on that generated type. Provide a minimal supported consumer recipe or link to an executable example, plus package/setup prerequisites; verify it with a clean consumer.
4. Low, contributor discoverability: README.md:142 links AGENTS.md as contributor guidance, although it largely sets agent/repository execution constraints. Consider a concise human-facing contribution entry covering initial setup, verification, patch/PR workflow and security reporting, linked from the README; reuse existing guides instead of duplicating authoritative details.
5. Low, current update procedure navigation: tools/README.md:96 and docs/plan/T24-ROOT-READINESS.md:30 link to the preview task report's update procedure, which combines runnable steps with historical pre-T26 status at docs/plan/T24-PREVIEW-READINESS.md:12-36. Move or consolidate the actively maintained procedure in the tool guide, link current T26 acceptance and retain the original task evidence. ReproducibilityDiff.cs:29 confirms that every diff still reports validationAcceptance false; that fact is not stale merely because T26 conformance was completed.
6. Low, readability of evidence tables: docs/plan/T20-HELPER-COMPATIBILITY.md:124-133 and docs/plan/T21-PREVIEW-DIFFERENTIAL.md:27-40 place many long test names into individual cells, making the exact requirement mappings difficult to scan at narrow widths. Consider grouped lists or smaller tables without dropping identifiers and evidence.

## Recommendations for follow-on research

- [ ] Complete a full read of the remaining 41 partially inspected Markdown documents and LICENSE; other reviewers may cover these.
- [ ] Check local links, fragments and Markdown rendering with available tooling; establish a documented baseline.
- [ ] Compile or run current consumer examples in clean projects and validate platform-specific commands separately.
- [ ] Verify whether a current architecture/contribution entry point exists elsewhere before adding new pages.

## Clarifying questions

None required to report the observations above.
