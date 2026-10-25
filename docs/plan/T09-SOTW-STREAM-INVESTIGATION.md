# T09 — SotW stream investigation

Investigated 25 October 2026 on T08 commit `95ca449a46963edebcda81d6101db1e6f16a84c3`. Behavior reference: `67c5345367582741334e34ffc66a561b0ce32946`. Investigation complete; the transport decision below was accepted on 25 October 2026. This records findings and implementation direction for [issue #10](https://github.com/knightfall/envoy-dotnet-control-plane/issues/10). **The C# stream engine and its comparison tests are not implemented yet.**

## Accepted transport decision

Use gRPC for ASP.NET Core with Kestrel when T11 adds the hosting adapters. [PR #45](https://github.com/knightfall/envoy-dotnet-control-plane/pull/45) merged the reproducible transport probe; its [final Linux run](https://github.com/knightfall/envoy-dotnet-control-plane/actions/runs/40179401045) passed 46 functional checks and completed 90 trials at `6bcdf28ef3da5d833cc489364b4556f0b5001308`. The [transport report](GRPC-KESTREL-VALIDATION.md) records the controls and configuration differences.

The user accepted the measured TLS throughput/CPU penalty for now and requested continuation with the waiting implementation. Performance tuning and the unresolved allocation-counter investigation do not block T09. This acceptance does not assert performance equivalence, validate the unreliable .NET allocation series or waive functional parity. Continue with the transport-independent SotW engine, then T10 ADS behavior and T11 adapters.

## Do we need a C# xDS implementation?

We need the server behavior in Go's `pkg/server/sotw/v3/xds.go`; that is T09. It belongs in Core alongside the existing cache, watch and subscription types. The file is an event loop, not another protocol definition or a separately packaged xDS dependency.

Protocol already contains generated `DiscoveryRequest`, `DiscoveryResponse`, Delta messages, discovery service contracts and the CNCF xDS types required by the selected schema closure. The current generated output includes eleven `Xds/` C# files. [The catalog](../../schemas/catalog.json) lists their source schemas; [the source lock](../../schemas/upstream.lock.json) pins CNCF xDS at `ee656c7534f5d7dc23d44dd611689568f72017a6`. This is the selected Envoy catalog's dependency closure, not every API in CNCF xDS.

The [CNCF repository](https://github.com/cncf/xds/blob/ee656c7534f5d7dc23d44dd611689568f72017a6/README.md) describes shared APIs and their relationship to Envoy's APIs. Its generated Go bindings do not require a second hand-written C# protocol layer. Continue generating the selected upstream schemas through Buf. T09 supplies runtime behavior; ordered ADS remains T10, service adapters T11 and hosting T12. No additional xDS package, replacement schemas or duplicate generated types are needed for T09.

## Source findings

Reviewed the [ordinary loop](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/xds.go), [callbacks, send and receive](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/server.go), [watch records](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/watches.go), [subscriptions](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/stream/v3/subscription.go) and the separate [ordered ADS path](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/ads.go).

| Operation | Required behavior |
|---|---|
| Open | Allocate a server-local stream ID and an initially empty Node. Register shutdown before invoking open, so rejected open still invokes close. |
| Normalize | Carry forward an omitted Node; retain an explicitly supplied later Node. Fill an absent per-type type URL. Missing ADS type URL fails before the request callback. A nonempty different type URL on a per-type call is retained. |
| Request callback | Invoke after normalization and before nonce checks. Use the resulting request for type dispatch, subscription and nonce checks; do not normalize it a second time. |
| Stale request | If the current type record has a nonempty nonce and the request nonce differs, ignore the request without replacing the watch or subscription. The callback has already seen it. |
| Accepted request | Cancel the old watch before changing its subscription. Reuse the subscription and install a new watch record with an empty nonce. An immediate result is valid. |
| Response selection | Resolve the current type record using the response's originating request type URL and check returned-name metadata against that subscription. Do not impose another response identity rule. |
| Send | Materialize, assign the next stream-wide decimal nonce, update returned metadata and the record nonce, invoke the response callback, then send serially. |
| End | Release current watches and invoke close once. Input end and receive errors end Go's loop normally; processing and send errors are returned. |

Important consequences:

- A matching ACK replaces the watch and clears that record's nonce. A subsequent request before its response is not rejected against an older nonce. A separate persistent nonce ledger would change the behavior.
- A NACK is visible to callbacks. The cache still uses the request version and subscription; the server does not roll back a snapshot or blacklist its version.
- Node capture precedes the stale check. Even an ignored stale request can replace the carried Node. Replacing `request.Node` inside the callback does not replace the already captured reference; mutating that Node object remains visible through the shared reference.
- The response callback receives the cache response's creation context. Open receives stream context. These contexts must not be substituted for each other.
- The record captures its nonce before the response callback. If the callback changes the outgoing nonce, the wire value changes but the record retains its earlier value. Preserve this order rather than repairing callback mutations.
- Ordinary Go selection imposes no fixed cross-type priority or drain-before-request rule. Ordered ADS has a separate path; its rules must not leak into T09.

## Small .NET implementation

Use a public `SotwServer` entry point, optional asynchronous `ISotwCallbacks` and a private per-call session. Accept an `IAsyncEnumerable<DiscoveryRequest>`, an asynchronous send delegate, an implied type URL and stream context. Reuse `SotwOptions`, `IConfigWatcher`, `IWatch<ISotwResponse>`, `ResponseContext` and `SotwSubscription`. Exact signatures remain subject to compilation and adapter consumption; this investigation does not freeze a public API.

Each session owns one request enumerator, at most one outstanding `MoveNextAsync`, one carried Node, one nonce counter and a dictionary of type records. Each record holds its watch, subscription, nonce and nullable task for the unconsumed completion. One processing loop selects a request, a current watch completion or cancellation and awaits callbacks and sends directly.

Clear a record's pending-completion task after consuming it, including a suppressed response. Retain its nonce/subscription until replacement or shutdown. Otherwise an already completed .NET task would be selected repeatedly and resend the same response. Recompute candidate tasks after request processing; a replaced record's task is no longer a candidate. This arrangement leaves no superseded completion in a shared queue and needs no generation identifier.

Start the next receive after taking the current request, before awaiting processing. That permits one held request while a callback or send is blocked, matching Go's reader handoff. Do not drain an unbounded input queue or start a second writer. Simultaneously ready events have no promised priority; comparison traces must establish event order explicitly.

Use linked cancellation for caller/host shutdown. Cleanup cancels and observes the owned receive, disposes the enumerator, releases every current watch and invokes close once. Close still runs after rejected open or a cleanup failure. Cancellation is cooperative: input, send and async callbacks must honor their cancellation contract. The core cannot force a non-cooperative application operation to stop. No cleanup timeout, detached worker or retry subsystem is proposed.

Clone the materialized `DiscoveryResponse` envelope with its generated `Clone()` before assigning nonce or invoking its callback. This isolates a cached/custom response body shared by two streams. `Any` envelopes and control metadata are copied; immutable payload `ByteString` values remain shared. Published resource graphs are neither deserialized nor cloned. Generated cloning preserves unknown fields without hand-rebuilding a partial envelope.

There is no writer service, dispatcher, mailbox, explicit state enum, ACK ledger, middleware chain, quota or configurable failure policy. The exception classification below describes failures, not stream state.

## Fixed failure and cleanup contract

Use one policy, with transport translation left to T11/T12:

| Outcome | Core result after cleanup |
|---|---|
| EOF or receive-operation failure | Complete normally, matching Go's reader closing its request channel. Do not mistake request-processing failure for receive failure. |
| Caller/host cancellation selected by the loop | Complete normally, matching Go's server-context exit. Suppress only expected cancellation of the session's own receive/wait. |
| Missing ADS type URL | Throw a small SotW protocol exception classified `InvalidArgument`. |
| Null request/result or unexpectedly canceled current watch | Throw the protocol exception classified `Unavailable`. |
| Malformed custom response: null materialized body, absent originating request/current type record | Throw `InvalidOperationException`; these are custom-response invariant failures. |
| Cache creation, faulted watch, materialization, callback or send exception | Preserve the original exception. Do not flatten it into an argument error or retry. Callback/send cancellation exceptions remain operation failures rather than being swallowed by a blanket cancellation catch. |
| Async response callback failure | Fail and clean up; nothing is sent afterward. This is a .NET adaptation because Go's hook cannot return an error. |
| Watch/iterator disposal or close callback failure | Attempt remaining cleanup and close once. Throw a sole failure unchanged; for several failures use `AggregateException`, with the processing failure first and cleanup failures afterward. |

The protocol exception needs only those two classifications, without an error registry or configurable mapping. Later adapters must not map every `InvalidOperationException` to an argument error. Iterator creation/disposal errors are .NET input/cleanup failures, distinct from receive-operation failure.

The receive-error distinction is deliberate source fidelity, not a claim that the transport reports success to a disconnected client. Actual gRPC status/cancellation behavior requires adapter tests. Async hooks must receive the cancellation information needed to honor shutdown while preserving response creation context. Close has no automatic retry or cancellation shortcut; applications must keep it bounded. Preserve callback/disposer failure evidence while attempting to release the other owned resources.

## Verification to implement

Reuse the existing Core test project and pinned Go oracle. Group tests by visible behavior and use explicit handoffs rather than sleeps or stress loops:

| Group | Discriminating observations |
|---|---|
| Requests and callbacks | Initial/omitted/later Node, stale-request Node capture, implied/custom/absent ADS type URL, effective request mutation and callback order. |
| Nonces and versions | Stale requests remain callback-visible without watch replacement; matching requests replace watches; nonce resets until the new response; NACK can re-deliver the current snapshot without rollback. |
| Responses | Metadata filtering precedes materialization; immediate results are installed and consumed once; creation context is retained; metadata precedes callback/send; callback mutations affect output. |
| Serial processing and reentry | One blocked send with one held receive, another type's pending watch, and callback publication through the real SnapshotCache. Callbacks and sends run outside cache synchronization. |
| Lifecycle | Open/request rejection, EOF, receive error, cancellation, creation/watch/materialization/send/callback failures; watches released and close once. One combined processing/cleanup failure proves primary-error retention. |
| Ownership | Two streams using a shared materialized body do not leak nonce/callback mutations; payload and unknown fields remain intact. |

Drive actual Go `StreamHandler` with controlled input/cache responses for differential traces. Compare callbacks, watch replacement/counts, node/type/version values, returned names and nonce relationships. Use barriers where Go could choose either ready event. Do not claim fixed cross-type priority or treat a rewritten private helper as an independent oracle.

T09 covers core portions of V10–V12, V15 and V29. V28 requires actual HTTP/2 service calls, so network acceptance stays with T11/T12/T22. Ordered ADS remains T10, heartbeat scheduling T17 and full lifecycle/network acceptance T22. Test count follows these cases; no implementation estimate is committed here.

## Observed investigation checks

On macOS arm64 with Go **1.26.4**, nine selected upstream test functions passed: **49 leaf results, 0 failures and 0 skips**. These comprise 45 subtests and four functions without subtests; the nine parent pass events are not counted again.

Functions in pinned `pkg/server/v3`: `TestServerShutdown`, `TestResponseHandlers`, `TestSendError`, `TestStaleNonce`, `TestAggregateRequestType`, `TestCancellations`, `TestOpaqueRequestsChannelMuxing`, `TestNilPropagationOverResponseChannelShouldCloseTheStream` and `TestCallbackError`. These exercise the existing source fixtures. The receive helper's error classification was read directly, not established by an additional custom trace.

Reproduce from the repository root in a temporary module so test dependency resolution does not change maintained oracle locks:

```sh
probe=$(mktemp -d)
cp tests/resources/GoOracle/go.mod tests/resources/GoOracle/go.sum "$probe/"
cd "$probe"
GOWORK=off go test -mod=mod -json github.com/envoyproxy/go-control-plane/pkg/server/v3 \
  -run '^Test(ServerShutdown|ResponseHandlers|SendError|StaleNonce|AggregateRequestType|Cancellations|OpaqueRequestsChannelMuxing|NilPropagationOverResponseChannelShouldCloseTheStream|CallbackError)$' \
  -count=1 > source-tests.json
```

The temporary module retains the existing root/Envoy commit pins and resolves additional upstream test dependencies there. It does not update repository dependencies or prove every schema/service in the aggregate Go server. The four reviewed SotW source files were also checked against their GitHub blob identities at the behavior pin.

No C# source, binding fingerprint, schema pin, package dependency or generated code changed. No .NET tests, new Go/.NET stream comparison, network test, package publication or CI result is claimed by this investigation. Existing T08 results remain in [T08 evidence](T08-SOTW-SUBSCRIPTIONS.md).
