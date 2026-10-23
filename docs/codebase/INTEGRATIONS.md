# Integrations

Reviewed 24 October 2026. Source paths below are relative to go-control-plane/.

## Upstream reference

- Envoy v3 Protobuf/gRPC discovery supports SotW, Delta, ADS, unary fetch, and the HTTP/JSON gateway.
- StreamRlsConfigs and FetchRlsConfigs are part of the root server; no dedicated rate-limit Delta method or HTTP route is declared.
- Envoy/contrib bindings come from a separate Envoy checkout. The generated rate-limit files identify schema paths but record no separate source revision.
- Only the matcher test proto is checked in as a .proto input.

## Proposed .NET integration

- Pin the binding provider and generated input catalog, including common Any extension messages.
- Support application-supplied IMessage/Any payloads and descriptor registration.
- Evaluate Buf SDKs or existing C# bindings before owning generation; exact schema/restore compatibility is [TODO].
- Include unary/HTTP adapters and the SotW ADS client helper in 1.0.
- Choose released Envoy image digests for interop independently of the latest schema pin.
- Keep standalone matcher, full legacy/contrib catalogs, and validator API parity separately scoped.
- Prefer the optional [Protovalidate adapter design](../architecture/VALIDATION.md), with a mandatory early provider/metadata assessment and a separate conformance gate before claiming Envoy validation.

## Evidence

- pkg/server/v3/server.go:44-62,226-357; gateway.go:38-98
- ratelimit/service/ratelimit/v3/rls_conf_ds.pb.go:172-174
- pkg/client/sotw/v3/client.go
- envoy/COMMIT; ci/sync_envoy.sh:23-49; xdsmatcher/test/proto/test.proto
- [Buf NuGet SDKs](https://buf.build/docs/bsr/generated-sdks/nuget/)
