# T06 — One-shot watches and custom contracts

Implemented 25 October 2026 on the T05 foundation merged in PR #39 (`840d0480768dc6242faac84d3c2bca3436f3d238`). Behavior reference: `67c5345367582741334e34ffc66a561b0ce32946`. This task supplies completion/cancellation primitives and contracts for custom watchers; actual cache publication and stream engines remain T07 onward.

## Source and .NET adaptation

The source contracts are [cache.go](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/cache.go), with [watch creation/cancellation](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/simple.go) and [watch metadata](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/cache/v3/status.go). The [normal SotW loop](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/xds.go) creates a capacity-one response channel before creating its watch, allowing an immediate result before the consumer starts reading. [Ordered ADS](https://github.com/envoyproxy/go-control-plane/blob/67c5345367582741334e34ffc66a561b0ce32946/pkg/server/sotw/v3/ads.go) uses a shared buffer and explicitly different backpressure; that engine remains T10.

The [Core Cache folder](../../src/Envoy.ControlPlane/Cache/) contains eight focused types:

- `IWatch<T>` exposes one `Completion` task and synchronous disposal. `Watch<T>` is its producer implementation, with result/error completion and optional registration cleanup.
- `ISubscription` retains returned resource versions, exact names, prefix globs and wildcard state. Its collections remain stable for a watch's lifetime; subscription transitions belong to T08/T13.
- `IConfigWatcher` supports typed SotW and Delta creation. It can return an immediate result, return a pending watch or throw a creation error synchronously.
- `IWatchResponse`, `ISotwResponse` and `IDeltaResponse` retain versions, returned metadata, originating typed requests and materialization methods. A custom response may fail during materialization after successful watch delivery.
- `ResponseContext` is one readonly value carrying cancellation and optional caller state. It adapts Go context without an ASP.NET dependency, ambient context registry or ownership policy.

A single atomic claim determines whether response, producer failure or disposal wins. The winner releases its registration exactly once and completes the task; later operations preserve that first outcome. Completion occurs in `finally` so cleanup failure cannot leave the claimed task pending. Producer cleanup must remain short and must not perform stream/network work. Task continuations run asynchronously, keeping consumer processing out of producer synchronization.

Consumers own disposal. The [package example](../../src/Envoy.ControlPlane/README.md) uses a disposal scope around `Completion.WaitAsync(cancellationToken)`. Canceling that wait alone does not cancel the underlying watch; leaving the scope releases it. No extra cancellation-registration mechanism, writer service, mailbox, generation counter, configurable queue or cloned subscription model is introduced.

The task is one result, corresponding to the immediate/pending watch contract. It does not model the ordered ADS shared buffer or prove stream backpressure. Default raw response serialization, TTL wrappers and Delta body construction remain their cache/stream tasks. Context metadata itself does not automatically cancel delivery.

## Tests and requirement mapping

Tests use the existing Core xUnit v3/native MTP project, central dependencies and Roslynator. Six watch methods expand to seven cases; three custom-contract cases bring the addition to ten. The source-to-test pairing scan covered the eight new production files before test implementation; that static inventory is not runtime coverage.

| Primitive requirement | Evidence |
|---|---|
| V08 immediate completion before awaiting | `ImmediateResponse_IsAvailableBeforeConsumerStartsAwaiting` |
| V08 pending result and duplicate delivery rejection | `PendingResponse_ReleasesRegistrationOnceAndKeepsFirstResult` |
| V29 producer failure and original exception | `ProducerFailure_PreservesExceptionAndReleasesRegistrationOnce` |
| V08/V29 repeated disposal and refused late result/failure | `Disposal_CancelsPendingWatchAndRejectsLaterDelivery` |
| V30 response-versus-cancel with cleanup in progress | `ClaimedOutcome_WinsWhileRegistrationCleanupIsInProgress`, both winner orders |
| V30 blocked consumer without producer/other-watch stall | `BlockedConsumer_DoesNotRunInsideProducerOrBlockOtherWatches` |
| R06 custom SotW/Delta, stable subscription, request and context retention | `CustomWatcher_RetainsRequestsSubscriptionMetadataAndResponseContext` |
| R06 custom materialization and creation failures | `MaterializationFailure_IsVisibleAfterSuccessfulWatchDelivery`, `CreationFailure_IsSynchronousAndPreservesCustomWatcherException` |

The competing operations use explicit gates and release those gates in `finally`. Bounded waits fail on a hang and honor the xUnit cancellation token. No sleep, stress loop or timing-success assertion is used. The blocked-consumer check requests a synchronous continuation, then blocks it; the producer must return and an unrelated watch must still complete. Removing asynchronous-continuation behavior would fail that check.

Static source/assertion review checked first-outcome retention, original exception identity, canceled task state, cleanup counts, duplicate result rejection and blocked-consumer progress. These are discriminating assertions, not an executed mutation-testing or coverage result. Custom response fixtures demonstrate consumption through the public interfaces; they do not claim a real cache or server implementation.

## Observed local verification

On macOS arm64 with pinned .NET SDK **10.0.400**, Go **1.26.4** and the unchanged Buf **1.75.0** binding inputs:

```sh
dotnet test --project tests/Envoy.ControlPlane.Tests/Envoy.ControlPlane.Tests.csproj --configuration Release --no-restore
dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify
```

The focused Core suite passed **48 cases, 0 failures and 0 skips**. The complete solution passed **102/102** cases; the fresh package suites passed Protocol **12**, Validation **18** and Core **48** cases, each with zero failures/skips. Independent Go snapshot **19/19** and existing Go PGV **33/33** comparisons matched. All five packages and five symbol packages passed content checks, and the separate Core/ASP.NET executable consumers passed. The complete solution build finished with **0 warnings and 0 errors**. Initial analyzer findings about test-cancellation tokens and documentation format were corrected; no suppression or dependency change was added. The first local build stalled before producing diagnostics; retrying with build-server connections disabled completed with zero warnings/errors.

`repository verify` discovers the same Core suite from the solution and stages every handwritten test/support source into its fresh NuGet consumer. Linux/macOS/Windows CI invokes the same entry points. No schema generation change, evaluator dependency or NuGet publication is part of T06.

## Remaining integration evidence

V08's no-snapshot, same-version pending and register-versus-publish cases require the real T07 cache and its shared synchronization. ADS missing-name/no-registration behavior is also T07. V29/V30 stream rejection, EOF, blocked writer/callback, shutdown and full cleanup remain T09/T10/T14/T22. T06 proves the watch primitives used by those features; it does not close their full scenario families or establish network interoperability.
