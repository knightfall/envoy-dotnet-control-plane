# Google.Protobuf runtime upgrade plan

## Objective and starting point

Evaluate and, if the evidence supports it, upgrade the centrally managed
`Google.Protobuf` runtime from 3.35.1 to 3.36.2 independently of the other
updates in [Dependabot PR #89](https://github.com/knightfall/envoy-dotnet-control-plane/pull/89).
Maintain the selected Envoy protocol contracts, generated binding ownership,
locked restores and existing validation scope. This plan describes work to do;
it is not evidence that the upgrade is compatible.

The isolated candidate has been implemented locally. See the separate
[compatibility record](../compatibility/protobuf-runtime-3.36.2.md) for the
observed results and remaining final-head CI and review requirements.

The planning branch starts at main commit `43e8e83a7ebb1561874d7315c61cfbc206df1ea9`.
Recheck the base and candidate versions before implementation. The runtime,
Envoy-selected Protobuf source, Buf C# generator, inspection tools and released
Envoy version are independent inputs and must be recorded separately.

At the repository's pinned go-control-plane commit
`67c5345367582741334e34ffc66a561b0ce32946`, both the
[root module](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/go.mod)
and [Envoy module](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/envoy/go.mod)
require `google.golang.org/protobuf v1.36.12`. This Go runtime uses a separate
version scheme and is not equivalent to C# `Google.Protobuf 3.36.2`. It provides
context for differential tests, not proof of C# compatibility. Protobuf's
cross-version policy makes the older generated C# with a newer same-major
runtime a plausible candidate, subject to its exceptions and the checks below.

## Evidence to investigate

On the grouped Dependabot branch, a refreshed set of project lock files allowed
locked solution restore and build. After fresh binding generation, the solution
test run had 1,247 passes and one failure: the source-to-generated descriptor
comparison for `google/protobuf/descriptor.proto`. The runtime's descriptor
contains differences including edition-related feature options and defaults.
This was observed with other dependency updates present, so reproduce it with
the Protobuf update isolated before attributing the failure to that update.
The failure establishes descriptor drift; it does not establish wire, API,
validation or released-Envoy incompatibility. The remaining repository and
proxy verification gates were not completed on that candidate.

The catalog uses original upstream schemas and gives Google.Protobuf ownership
of well-known types. The pinned source and C# generator are currently at 35.1;
the runtime package is 3.35.1. The [Protobuf cross-version runtime guarantee](https://protobuf.dev/support/cross-version-runtime-guarantee/)
provides context for generated-code/runtime combinations, but does not prove
that their well-known descriptors are identical or that this repository's
specific contracts are compatible.

## Work and decision points

1. **Establish the baseline.** From a clean main-based checkout, follow
   [tools/README.md](../../tools/README.md) to generate bindings from a fresh
   external source cache and record the existing pinned inputs and verification
   results. Save an upstream report outside the generated output directory.
   Record the exact SDK, platform, source and tool pins, package version and
   lock-file state. Keep existing published compatibility evidence unchanged.

2. **Isolate the candidate.** Review the [3.36.2 release](https://github.com/protocolbuffers/protobuf/releases/tag/v36.2)
   and package provenance. Change only the central runtime dependency and the
   necessary NuGet lock files; do not take the grouped gRPC updates along with
   it. Refresh locks using a deliberate non-locked restore, inspect the full
   diff, then require locked restore. Reproduce the descriptor comparison and
   record a second upstream report and the exact candidate inputs.

3. **Characterize descriptor drift.** Compare all Google.Protobuf-owned
   well-known descriptors against the selected source, including options,
   extensions, field identity and edition metadata. Classify each difference
   and its implications for reflection, `Any`, JSON, validation and callers.
   Verify that all other selected descriptors and preserved annotations retain
   their existing checks. Trace ownership and generated-code/runtime version
   requirements to upstream documentation and the actual package artifacts.

4. **Choose an evidence-backed resolution.** Prefer retaining the selected
   Envoy source and generator if they work with the newer runtime. If runtime
   descriptors necessarily differ, consider a narrowly specified,
   version-bound comparison that verifies the known differences and rejects
   unexpected additions, removals and changes. Preserve the original source
   descriptors and single owner; do not blanket-exclude well-known types or
   weaken option and annotation checks. If the difference cannot be safely
   characterized, investigate a separately reviewed source/generator update
   against the actual upstream selection and provenance. Do not move the source
   baseline merely to make the test pass. Stop the upgrade if neither path can
   establish the required contract.

5. **Resolve inspection and provenance.** The schema inspector verifies the
   loaded Protobuf assembly against its inspection-tool lock. Determine whether
   to keep an independently pinned inspector runtime or update its package and
   assembly hashes to the verified candidate. Prove the chosen arrangement can
   inspect the pinned source without changing ownership or confusing the
   inspection runtime with the source/protoc pins. Update only the applicable
   manifests, fingerprints and generated outputs through maintained tooling.
   The inspector's declared platform support must be respected.

6. **Add targeted regression coverage.** Preserve existing assertions. Cover
   each accepted descriptor difference and reject unexpected drift, including
   altered and removed fields/options; verify one owner for each well-known
   type. Exercise representative binary, JSON, `Any`, reflection and preserved
   validation-option behavior, including meaningful failure cases. Verify the
   validation evidence remains tied to all declared inputs and becomes stale
   when a relevant input changes; regenerate evidence only by running its
   verification, never by editing fingerprints to manufacture a pass.

7. **Run separate compatibility gates.** Run the documented provenance,
   binding and complete repository verification commands, including locked
   restores, build, tests, Go comparisons, packaging and clean package-consumer
   checks. Inspect consumer dependency resolution and public/API behavior,
   including consumers that resolve the candidate runtime. Run optional
   validation conformance and the released-Envoy gate on supported platforms as
   separate evidence. Report exact versions, platform, pass/fail/skip counts,
   analyzer results and any unrun gate; do not infer interoperability from unit
   tests or source comparisons.

8. **Review and document the outcome.** Write a new compatibility record with
   the exact source, generator, runtime, inspection and proxy pins; descriptor
   differences; resolution and rationale; observed results; and remaining
   limitations. Keep earlier release evidence intact. Check current-head CI
   and automatic PR review before any merge. Coordinate the overlapping
   Dependabot PR and lock-file repair after the isolated result is known;
   consider a temporary Dependabot exclusion only if needed to avoid duplicate
   updates, and explain when it will be removed.

## Acceptance criteria

- The candidate is isolated, provenance is verifiable, every affected lock
  file is current, and locked restores succeed.
- The selected contract and ownership are preserved, or a separately reviewed
  baseline change has an upstream rationale and its own compatibility proof.
  Descriptor differences are explicit and unexpected drift fails verification.
- Applicable repository, clean-consumer and validation gates pass with their
  evidence bound to the final inputs. Released-Envoy and broader differential
  results are reported separately and are not claimed if not run.
- Any unresolved compatibility issue blocks the upgrade and is documented
  with a reproducible result rather than bypassed.
