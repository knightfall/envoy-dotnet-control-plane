using System.Text.Json;
using System.Threading.Channels;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests.Support;

internal static class MuxCacheCorpus
{
    internal static MuxCacheInput[] Cases() =>
    [
        new("sotw-snapshot", "sotw", "snapshot", "linear") { ExpectedVersion = "snapshot/v1", ExpectedReady = true },
        new("sotw-linear", "sotw", "linear", "snapshot") { ExpectedVersion = "1", ExpectedReady = true },
        new("delta-snapshot", "delta", "linear", "snapshot") { ExpectedVersion = "snapshot/v1", ExpectedReady = true },
        new("delta-linear", "delta", "snapshot", "linear") { ExpectedVersion = "1", ExpectedReady = true },
        new("cancel-sotw-snapshot", "sotw", "snapshot", "linear") { Cancel = true },
        new("cancel-delta-linear", "delta", "snapshot", "linear") { Cancel = true },
        new("missing-sotw", "sotw", "missing", "snapshot") { ExpectedError = "missing" },
        new("missing-delta", "delta", "snapshot", "missing") { ExpectedError = "missing" },
        new("missing-fetch", "fetch", "missing", "snapshot") { ExpectedError = "missing" },
        new("custom-fetch", "fetch", "custom", "missing") { ExpectedVersion = "custom/v1", ExpectedReady = true },
        new("custom-fetch-error", "fetch", "custom", "missing") { FetchFailure = true, ExpectedError = "child-fetch" },
        new("linear-fetch", "fetch", "linear", "custom") { ExpectedError = "linear-fetch" },
    ];

    internal static async Task<MuxCacheResult> EvaluateAsync(MuxCacheInput input)
    {
        var snapshot = new SnapshotCache();
        var linear = new LinearCache(ResourceTypeUrls.Route);
        var custom = new MuxProbeCache();
        var request = Request(ResourceTypeUrls.Route, "0");
        if (input.FetchFailure)
        {
            custom.FetchCompletion.SetException(new InvalidOperationException("fixture child failure"));
        }
        else
        {
            custom.FetchCompletion.SetResult(new TestSotwResponse(request, "custom/v1", new Dictionary<string, string>(), default,
                () => new DiscoveryResponse { TypeUrl = ResourceTypeUrls.Route, VersionInfo = "custom/v1" }));
        }
        int sotwCalls = 0;
        int deltaCalls = 0;
        var mux = new MuxCache(new Dictionary<string, IConfigWatcher> { ["snapshot"] = snapshot, ["linear"] = linear, ["custom"] = custom },
            _ =>
            {
                sotwCalls++;
                return input.SotwKey;
            }, _ =>
            {
                deltaCalls++;
                return input.DeltaKey;
            });
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        var deltaResponses = Channel.CreateBounded<IDeltaResponse>(1);
        IDisposable? registration = null;
        DeltaCacheFrame? frame = null;
        int beforeSnapshot = 0;
        int beforeLinear = 0;
        int afterSnapshot = 0;
        int afterLinear = 0;
        string error = "";
        try
        {
            if (input.Mode == "fetch")
            {
                frame = Observe(await mux.FetchAsync(request));
            }
            else
            {
                registration = input.Mode == "delta"
                    ? mux.CreateDeltaWatch(new DeltaDiscoveryRequest { TypeUrl = ResourceTypeUrls.Route, Node = request.Node, ResponseNonce = "accepted" }, new DeltaSubscription(["a"]), deltaResponses.Writer)
                    : mux.CreateWatch(request, new SotwSubscription(["a"]), responses.Writer);
                beforeSnapshot = CountSnapshotWatches(snapshot);
                beforeLinear = linear.NumCacheWatches;
                if (input.Cancel)
                {
                    await ((IAsyncDisposable)registration).DisposeAsync();
                }
                await snapshot.SetSnapshotAsync("node", new Snapshot("snapshot/v1", new Dictionary<string, IEnumerable<IMessage?>>
                {
                    [ResourceTypeUrls.Route] = [new RouteConfiguration { Name = "a" }],
                }));
                await linear.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" });
                if (responses.Reader.TryRead(out var response))
                {
                    frame = Observe(response);
                }
                if (deltaResponses.Reader.TryRead(out var delta))
                {
                    var envelope = delta.GetDeltaDiscoveryResponse();
                    frame = new DeltaCacheFrame(envelope.SystemVersionInfo, envelope.TypeUrl,
                        envelope.Resources.Select(resource => new DeltaCacheResource(resource.Name, resource.Version, resource.Resource_.Value.ToBase64(), resource.Ttl is not null))
                            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray(),
                        envelope.RemovedResources.Order(StringComparer.Ordinal).ToArray(), delta.ReturnedResources.ToDictionary(StringComparer.Ordinal));
                }
            }
        }
        catch (KeyNotFoundException)
        {
            error = "missing";
        }
        catch (NotSupportedException exception) when (exception.Message == "Fetch is not implemented by LinearCache.")
        {
            error = "linear-fetch";
        }
        catch (InvalidOperationException exception) when (exception.Message == "fixture child failure")
        {
            error = "child-fetch";
        }
        finally
        {
            afterSnapshot = CountSnapshotWatches(snapshot);
            afterLinear = linear.NumCacheWatches;
            if (registration is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
        }
        return new MuxCacheResult(input, new MuxCacheObservation(frame, error, sotwCalls, deltaCalls, custom.Calls,
            beforeSnapshot, beforeLinear, afterSnapshot, afterLinear));
    }

    internal static void WriteResults(List<MuxCacheResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "mux-cache-corpus.json"),
        JsonSerializer.Serialize(results, DeltaCacheCorpus.JsonOptions) + "\n");

    private static int CountSnapshotWatches(SnapshotCache cache)
    {
        var status = cache.GetStatusInfo("node");
        return status is null ? 0 : status.NumWatches + status.NumDeltaWatches;
    }

    private static DeltaCacheFrame Observe(ISotwResponse response)
    {
        var envelope = response.GetDiscoveryResponse();
        return new DeltaCacheFrame(envelope.VersionInfo, envelope.TypeUrl,
            envelope.Resources.Select(resource => new DeltaCacheResource("", "", resource.Value.ToBase64(), false))
                .OrderBy(resource => resource.Data, StringComparer.Ordinal).ToArray(), [], response.ReturnedResources.ToDictionary(StringComparer.Ordinal));
    }
}

internal sealed record MuxCacheInput(string Id, string Mode, string SotwKey, string DeltaKey)
{
    public bool Cancel { get; init; }
    public bool FetchFailure { get; init; }
    public bool ExpectedReady { get; init; }
    public string ExpectedVersion { get; init; } = "";
    public string ExpectedError { get; init; } = "";
}

internal sealed record MuxCacheObservation(DeltaCacheFrame? Response, string Error, int SotwClassifications, int DeltaClassifications,
    int CustomCalls, int BeforeSnapshot, int BeforeLinear, int AfterSnapshot, int AfterLinear);
internal sealed record MuxCacheResult(MuxCacheInput Input, MuxCacheObservation Observed);
