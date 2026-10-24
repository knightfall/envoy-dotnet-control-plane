# Upstream schema licenses and notices

The [source lock](upstream.lock.json) records the SHA-256 of every root license/notice file below, plus the checksummed archive containing the original text. Source archives are retained outside this repository. Generated language bindings and redistributed schema sources have not been published.

| Source | Declared license | Original files at the pinned revision |
|---|---|---|
| Go control plane | Apache-2.0 | [LICENSE](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/LICENSE) |
| Envoy API | Apache-2.0 | [LICENSE](https://github.com/envoyproxy/envoy/blob/82a4424f760b6ba3ffc9f2911389d061656f35c8/LICENSE), [NOTICE](https://github.com/envoyproxy/envoy/blob/82a4424f760b6ba3ffc9f2911389d061656f35c8/NOTICE) |
| Rate-limit API | Apache-2.0 | [LICENSE](https://github.com/envoyproxy/ratelimit/blob/0d9cfd0b3934c6c66bd97544592f3f539e7138cc/LICENSE) |
| CNCF xDS / UDPA | Apache-2.0 | [LICENSE](https://github.com/cncf/xds/blob/ee656c7534f5d7dc23d44dd611689568f72017a6/LICENSE) |
| PGV | Apache-2.0 | [LICENSE](https://github.com/bufbuild/protoc-gen-validate/blob/92b9a7df69ca9f71bfc492f7a90adf4d36eab569/LICENSE) |
| Protobuf | BSD-3-Clause | [LICENSE](https://github.com/protocolbuffers/protobuf/blob/35cd01f9fe9afbeea38cc7b979a3b6bfcde82c03/LICENSE) |
| Google APIs | Apache-2.0 | [LICENSE](https://github.com/googleapis/googleapis/blob/2193a2bfcecb92b92aad7a4d81baa428cafd7dfd/LICENSE) |
| OpenTelemetry proto | Apache-2.0 | [LICENSE](https://github.com/open-telemetry/opentelemetry-proto/blob/c0a98a1847d3124ac5f9ecd02d0e2d2732bbb590/LICENSE) |
| Prometheus client model | Apache-2.0 | [LICENSE](https://github.com/prometheus/client_model/blob/eb136e513d419e0c31ad750922f0a6f7675c2dee/LICENSE), [NOTICE](https://github.com/prometheus/client_model/blob/eb136e513d419e0c31ad750922f0a6f7675c2dee/NOTICE) |
| CEL specification | Apache-2.0 | [LICENSE](https://github.com/google/cel-spec/blob/7f3c4c513b42d471d0be9439bf5bde38f45f8404/LICENSE) |

These twelve files cover the selected upstream schema sources and Go reference. The inspection-only Google.Protobuf 3.35.1 package declares BSD-3-Clause and identifies the same protobuf commit in its NuGet manifest; its package and loaded-assembly hashes are recorded separately in `inspectionTools`. The protoc inspection release comes from that protobuf version as well. Roslynator.Analyzers 5.0.1 declares Apache-2.0, with its exact package URL/checksum also locked. The installed .NET SDK has its own license and third-party notices; it is a tooling prerequisite, not a redistributed schema input.

T02 generates through [Buf CLI 1.75.0](https://github.com/bufbuild/buf/blob/v1.75.0/LICENSE), licensed Apache-2.0, using the official protobuf C# v35.1 and gRPC C# v1.84.0 remote plugins. Their upstream generator licenses are [protobuf BSD-3-Clause](https://github.com/protocolbuffers/protobuf/blob/v35.1/LICENSE) and [gRPC Apache-2.0](https://github.com/grpc/grpc/blob/v1.84.0/LICENSE). These tools are not bundled in Protocol. The runtime dependencies are Google.Protobuf 3.35.1 (BSD-3-Clause) and Grpc.Core.Api 2.84.0 (Apache-2.0); analyzers stay private build dependencies.

The source exporter copies the twelve original license/notice texts from the verified archives into its build output. The candidate package carries them under `licenses/<source-id>/`, alongside this inventory and the source locks. T03/T26 must record validation-provider and derived-metadata licenses. T24 reviews notices again against final package contents before release.
