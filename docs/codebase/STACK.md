# Stack

Reviewed 24 October 2026 at the Go and Envoy commits in ../../upstream-baseline.json. Source paths below are relative to the go-control-plane checkout.

## Upstream reference

- Go 1.26.0 is declared by the root module; gRPC-Go and Google Protobuf supply protocol/runtime support.
- envoy/, contrib/, and ratelimit/ contain generated APIs. Envoy and contrib outputs are mirrored using a separately pinned Envoy checkout.
- testify, go-cmp, and goleak are test dependencies.

## Proposed .NET stack

- Recommend .NET 10 for runtime packages, Google.Protobuf messages, standard generated gRPC contracts, and an ASP.NET Core hosting package.
- Separate protocol, runtime, and host responsibilities; use ILogger and CancellationToken.
- Binding provider and exact compiler/runtime versions remain [TODO] until a clean generate-or-restore/pack/consume proof.
- Prefer optional Protovalidate runtime validation; community .NET candidates exist. [Metadata migration and conformance](../architecture/VALIDATION.md) must be proven for Envoy's PGV annotations; standard message generation does not supply validator equivalence.

## Evidence

- go.mod:1-31; envoy/go.mod; envoy/COMMIT; ci/sync_envoy.sh
- [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Protobuf C# guide](https://protobuf.dev/reference/csharp/csharp-generated/)
- [Buf NuGet SDKs](https://buf.build/docs/bsr/generated-sdks/nuget/)
- [PGV generators](https://github.com/bufbuild/protoc-gen-validate#parameters)
