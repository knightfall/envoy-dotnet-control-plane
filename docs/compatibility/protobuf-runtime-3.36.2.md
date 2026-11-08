# Google.Protobuf 3.36.2 compatibility record

This record covers the proposed runtime update on top of main commit
`43e8e83a7ebb1561874d7315c61cfbc206df1ea9`; it is not a published release
record. The [upgrade plan](../plan/PROTOBUF-RUNTIME-UPGRADE.md) describes the
scope and remaining gates. Earlier release records remain unchanged.

## Version boundaries

- The pinned go-control-plane root and Envoy modules require Go's
  `google.golang.org/protobuf v1.36.12`. Go and C# runtime versions use
  different schemes and are not interchangeable.
- The selected Protobuf source and official Buf C# generator remain at 35.1.
  The .NET runtime and the inspection runtime are updated to 3.36.2. Their
  package and assembly fingerprints are recorded in the source lock; the
  inspection compiler remains independently pinned to 35.1.
- Google.Protobuf remains the sole owner of the selected well-known types.
  The Protobuf [cross-version guarantee](https://protobuf.dev/support/cross-version-runtime-guarantee/)
  describes the general support window; it does not prove this repository's
  descriptor or interoperability results.

## Descriptor difference

The 3.36.2 runtime embeds a newer `google/protobuf/descriptor.proto` than the
selected 35.1 source. The observed normalized differences are the addition of
edition-2026 removal metadata for `FileOptions.cc_enable_arenas`, an
edition-2026 naming-style default, an edition-2026 symbol-visibility default,
and the `FeatureSet.enforce_proto_limits` field and `ProtoLimitsFeature` enum.
No conclusion about wire compatibility follows from descriptor equality alone.

The descriptor test continues to check effective JSON names and all selected
descriptors and options. For this one file only, it accepts a specific pair of
SHA-256 fingerprints of the **entire** normalized source and runtime
descriptors. Either side changing causes a failure. A regression test mutates
source and runtime fields and runtime options to check that unexpected drift is
rejected. The source catalog and original annotation definitions are unchanged.

## Local evidence

On macOS arm64 with SDK 10.0.400, the pinned inspection compiler compiled 523
schemas, and both rate-limit descriptor comparisons passed. Binding generation
preserved 523 selected descriptors and matched 1,053 Go PGV fixture outcomes.

The first full repository run had 1,248 passes and one failure: a denied Delta
authorization call returned `Cancelled` instead of `PermissionDenied`. The
focused authorization rerun passed all three cases. A subsequent complete
repository verification passed: 1,249 solution tests, no failures or skips,
zero build warnings/errors, the declared Go comparisons, all five packages and
clean Protocol, validation, Core and hosting consumers. The clean consumer test
counts were 15, 738, 269 and 109 respectively, each with zero failures/skips.
The first failure is recorded as an observed intermittent result; its cause
has not been established.

The separate validation verification passed 738 tests and a clean validation
consumer with 738 tests. Its pinned conformance corpus passed 2,748 cases with
seven declared clock exclusions; 373 selected PGV fixtures passed, with 680
outside the declared rule scope. Go comparisons covered 68 whole-message and
310 field cases, representing 59 rule paths and 86 combinations.

The separate released-Envoy gate passed all 35 SotW/ADS, Delta/ADS and TTL
checks against the locked Envoy 1.40.1 image on a Linux arm64 Docker engine.
After those successful runs, the separate `bindings verify` command failed
twice during remote plugin generation: the Buf server was unavailable on the
first attempt and returned a rate-limit error on the second. That command did
not complete; rerun it when the service permits, and regenerate the build
inputs if necessary. The earlier generation and repository results above are
independent observed runs, not a substitute claim that this gate passed.
This evidence was collected from an uncommitted working tree, not a final PR
head. CI and automatic review on the final head remain required; Linux amd64
and Windows were not tested locally.
