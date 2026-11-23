# Documentation accuracy and staleness research

## Questions

Compare documentation with current source, configuration and release evidence, distinguishing historical milestones from current guidance.

## Findings

1. Current tooling guidance in tools/README.md:73 gives Coverlet.MTP 10.1.0 and ReportGenerator 5.5.11; Directory.Packages.props:18 pins 10.1.1 and .config/dotnet-tools.json:5-6 pins 5.6.0.
2. tests/README.md:28 gives xunit.v3.mtp-v2 4.0.2 and Microsoft Testing Platform 2.5.1; tests/Envoy.ControlPlane.Upstream.Tests/packages.lock.json:23-26,142-145 records 4.0.3 and 2.5.2.
3. docs/plan/T17-TTL-HEARTBEATS.md:22 says heartbeat writes begin under cache synchronization. src/Envoy.ControlPlane/Cache/SnapshotCache.Heartbeats.cs:103-121 instead creates delivery delegates under the lock and invokes delivery afterwards. Unlike most task reports, this one lacks a historical-scope banner. Link current docs/public-api.md:27,31,41 or update the present-tense description. Its line 30 gives TimeProvider.Testing 10.11.0 while Directory.Packages.props:19 currently pins 10.11.1; preserve historical versions as time-bound evidence.
4. Conditional release-verification concern: src/Envoy.ControlPlane.Protocol/README.md:32-37 and sibling package READMEs instruct gh attestation verify without --bundle. Current .github/workflows/publish.yaml:139-162 sets create-storage-record false; .github/release/publish.ps1:31-33 saves signed bundles as assets; .github/release/verify-attestations.py:48-51 passes --bundle. Conversely .github/release/verify-published-candidate.py:30-33 verifies the release manifest without a bundle and docs/releases/PUBLISHING.md records successful recovery. The workflow at the released commit and live GitHub record availability were not verified, so this is a question, not a proven failure.

## Coverage and limitations

Inventory used rg --files. Fully inspected README.md, SECURITY.md, scripts/README.md, docs/preview/README.md, docs/releases/1.0.0-rc.1.md, docs/releases/PUBLISHING.md, examples/Envoy.ControlPlane.Example/README.md, the Protocol, Validation and Validation.Annotations package READMEs, and tools/README.md. Fully read the following plan reports in bounded batches: T04-BUILD-FOUNDATION.md, T05-RESOURCES-AND-SNAPSHOTS.md, T06-WATCHES.md, T07-SNAPSHOT-CACHE.md, T08-SOTW-SUBSCRIPTIONS.md, T09-SOTW-STREAM-ENGINE.md, T09-SOTW-STREAM-INVESTIGATION.md, T10-ORDERED-ADS.md, T11-ASP-NET-HOST.md, T13-DELTA-SUBSCRIPTIONS.md, T14-DELTA-CACHE.md, T14-DELTA-GRPC.md, T14-DELTA-STREAMS.md, T15-LINEAR-CACHE.md, T16-MUX-CACHE.md and T17-TTL-HEARTBEATS.md. There is no separately named T12 Markdown report under docs/plan. T04 through T15 task reports explicitly identify themselves as historical; their outdated implementation status and test counts are retained as milestone evidence, and no additional confirmed current-facing issue was found in them.

Inspected portions of docs/codebase, docs/public-api.md, docs/compatibility, the Core and hosting package READMEs, tests/README.md, other plan reports, schemas and test support documents. This is not a complete line-by-line review of all documentation. Compared selected claims with source, locks and configuration. No examples, tests, CI, live publication, release asset or link verification was performed. gh returned operation not permitted; git status failed with a local xcrun developer-tools error.

## Recommended follow-on research

- [ ] Complete line-by-line review of remaining plan reports, schema and test support docs, documenting coverage.
- [ ] Inspect release workflow at its published commit and live GitHub attestation records; reproduce the README command and bundle-based alternative.
- [ ] Execute examples and verify links in an appropriately provisioned environment.

## Clarifying questions

None for the completed findings.
