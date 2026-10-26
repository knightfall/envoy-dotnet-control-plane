# T17 TTL wrapping and SotW heartbeats

`SnapshotCache.RunHeartbeatsAsync(interval, context)` runs a caller-owned asynchronous heartbeat loop using `PeriodicTimer` and the cache's existing `TimeProvider`. Applications start it explicitly, supervise its task, cancel its context and await completion. The timer is disposed as the loop ends; the cache and remaining watches stay usable. No constructor starts a background task, and Core requires no hosting dependency.

```csharp
using var stopping = new CancellationTokenSource();
Task heartbeats = cache.RunHeartbeatsAsync(
    TimeSpan.FromSeconds(1), new ResponseContext(stopping.Token));
// Run the application and publish snapshots.
stopping.Cancel();
await heartbeats;
```

Intervals must be positive and satisfy `PeriodicTimer`'s millisecond/range constraints. The application owns one loop invocation for a cache; no timer registry or scheduling policy is added. Tick coalescing uses the runtime primitive. Operational failures fault the task; cooperative context cancellation completes it after in-flight writes settle.

## Source behavior

Each tick scans pending **SotW** watches. It selects TTL entries covered by wildcard or exact names, deduplicating overlaps. The pinned source omits prefix-only selection; that limit is retained. Missing snapshots, missing names and non-TTL entries produce no heartbeat. A present zero TTL remains distinct from no TTL. Existing ADS readiness/untracked-watch behavior is preserved.

A heartbeat keeps the current collection version and the watch's returned-resource metadata. It wraps each selected resource with its name and TTL, omitting the payload and skipping payload serialization. Normal TTL responses retain their payload and lazy materialization. Wrapper resource versions remain empty at this source pin. Successful delivery releases the one-shot watch; the client must create another watch. Heartbeats do not update request-time diagnostics or delete expired resources from the cache.

Publication uses the existing semaphore and registrations. Writes begin under cache synchronization and await capacity outside it. Loop cancellation retains a failed pending watch, and awaiting the loop settles its writes. Watch disposal cancels only that registration and keeps the shared destination open. Snapshot publication remains usable after stopping heartbeats.

Raw Delta output still omits TTL at the pinned baseline, and no automatic Delta heartbeat is added. Caller-prebuilt `PassthroughDeltaResponse` envelopes preserve TTL and payload-free fields as supplied. T17 does not add TTL-sensitive Delta hashes or a new protocol extension. Live proxy refresh/expiry is the separate T23/V33 gate.

## Verification and provenance

Behavior pin: [`67c5345367582741334e34ffc66a561b0ce32946`](https://github.com/envoyproxy/go-control-plane/tree/67c5345367582741334e34ffc66a561b0ce32946). Source SHA-256 values: `simple.go` = `3709788d7edbfa47369513a93ef355cbb2b6fa8074d7e69074f385033ca7eb6a`; `cache.go` = `3b25b56c1adcb240afe1d070ddf14a93525167bba57e4d3210bd291560e3a866`. The scan adapts `sendHeartbeats` and the response materializer, retaining Apache-2.0 attribution. The existing original license is already included in Core.

Tests use the official [Microsoft.Extensions.TimeProvider.Testing 10.11.0](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/10.11.0), centrally pinned and private to Core tests, with its lock-file content hash. Runtime dependency graphs are unchanged. [Microsoft's TimeProvider guidance](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview) describes the built-in abstraction and fake-time package.

Thirteen shared cases compare actual Go `NewSnapshotCacheWithHeartbeating` output. An eligible sentinel proves that a real scan occurred; source cache synchronization provides the end-of-scan barrier. Tests do not use a sleep as proof of absence or copy a scheduler into the driver. The native side advances Microsoft's fake clock explicitly. Both sides compare wire payloads, TTL presence/value, wrapper versions, returned metadata, resource retention and pending counts, normalizing only unspecified resource order.

| Requirement | Native test |
|---|---|
| Normal TTL/non-TTL, zero TTL, exact/wildcard overlap, prefix/missing/ADS boundaries (V21/V22) | `SourceCases_MatchDeclaredOutcomesAndExportActualObservations` — thirteen actual Go comparisons |
| Explicit enablement, repeat watches, stable versions/metadata, stop and no cache expiry | `FakeTime_RefreshesRepeatedWatchesWithoutAdvancingVersionsOrExpiringSnapshots` |
| Prefix/non-TTL/missing watches remain pending after an observed scan | `PrefixAndNonTtlWatches_RemainPendingAfterAnObservedScan` |
| In-flight shutdown retains a watch and permits normal publication (V29) | `BlockedHeartbeat_StopRetainsWatchAndAllowsNormalPublication` |
| Owned write disposal and shared-writer lifetime | `WatchDisposal_SettlesBlockedHeartbeatWithoutClosingTheWriter` |
| Zero/negative intervals | `InvalidInterval_IsRejected` |
| Payload-free serialization and normal-response lazy failures | `HeartbeatPayload_IsNotSerialized_ButNormalTtlResponseIsLazy` |
| Delta independence and caller-owned TTL envelopes | `TimerShutdown_DoesNotCancelDeltaWatches_AndPassThroughRetainsCallerTtl` |

Nine new cases bring Core to **220 tests** and solution discovery to **364 tests**. Focused native tests and all thirteen actual Go comparisons passed on macOS arm64. Full local `repository verify` also passed: 364 solution tests, all Go corpora, five package/symbol pairs, fresh Protocol 14 / Validation 18 / Core 220 / Hosting 53 test consumers and both executable package-graph consumers. Compiler/Roslynator warnings/errors were zero; the three existing non-packable notices remain. An altered TTL payload observation was rejected. Exact-head GitHub CI/review is recorded separately on the implementing PR. Source/schema/generator pins and fingerprints remain unchanged.

After binding generation, `dotnet run --project tools/Envoy.ControlPlane.Tools --configuration Release -- repository verify` includes these tests, actual Go comparisons, packages and fresh consumers. Reports are retained under Protocol's `obj/upstream/results/heartbeats/go-results.json`. This cache/timer evidence does not substitute for V33 proxy refresh/expiry proof.
