using System.Text.Json;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.Service.Discovery.V3;
using Xunit;

namespace Envoy.ControlPlane.Tests.Support;

internal static class FetchEngineCorpus
{
    internal static FetchEngineInput[] Cases() =>
    [
        new("custom-success", "success", ["request", "fetch", "materialize", "response"]),
        new("request-rejected", "request", ["request"], "request"),
        new("cache-failure", "cache", ["request", "fetch"], "cache"),
        new("missing-envelope", "missing", ["request", "fetch"], "missing"),
        new("materialize-failure", "materialize", ["request", "fetch", "materialize", "response:null"], "materialize"),
        new("response-hook-failure", "response-hook", ["request", "fetch", "materialize", "response"], "response-hook"),
        new("request-mutation", "mutate-request", ["request", "fetch", "materialize", "response"]),
        new("response-mutation", "mutate-response", ["request", "fetch", "materialize", "response"]),
        new("caller-correlation", "different-origin", ["request", "fetch", "materialize", "response"]),
    ];

    internal static async Task<FetchEngineResult> EvaluateAsync(FetchEngineInput input)
    {
        var probe = new FetchProbe();
        var request = new DiscoveryRequest { TypeUrl = "input/type" };
        using var stop = new CancellationTokenSource();
        var context = new ResponseContext(stop.Token, new object());
        var failure = new InvalidOperationException("fixture-" + input.Mode);
        probe.BeforeRequest = current =>
        {
            if (input.Mode == "request") throw failure;
            if (input.Mode == "mutate-request") current.TypeUrl = "hook/type";
        };
        probe.Produce = current =>
        {
            if (input.Mode == "cache") throw failure;
            if (input.Mode == "missing") return null!;
            var origin = input.Mode == "different-origin" ? new DiscoveryRequest { TypeUrl = "other-origin" } : current;
            return new TestSotwResponse(origin, "v1", new Dictionary<string, string>(), context, () =>
            {
                probe.Events.Add("materialize");
                if (input.Mode == "materialize") throw failure;
                return new DiscoveryResponse { TypeUrl = current.TypeUrl, VersionInfo = "v1" };
            });
        };
        probe.BeforeResponse = response =>
        {
            if (input.Mode == "response-hook") throw failure;
            if (input.Mode == "mutate-response") response!.VersionInfo = "from-hook";
        };
        DiscoveryResponse? outgoing = null;
        string error = "";
        try
        {
            outgoing = await new FetchServer(probe, probe).FetchAsync(request, context);
        }
        catch (InvalidOperationException caught)
        {
            if (input.Mode == "missing") Assert.Equal("Missing response.", caught.Message);
            else Assert.Same(failure, caught);
            error = caught.Message == "Missing response." ? "missing" : caught.Message["fixture-".Length..];
        }
        Assert.Equal(input.ExpectedError, error);
        Assert.Equal(input.ExpectedEvents, probe.Events);
        Assert.Equal(input.ExpectedError.Length == 0, outgoing is not null);
        if (probe.LastRequest is not null) Assert.Same(request, probe.LastRequest);
        if (probe.ResponseRequest is not null) Assert.Same(request, probe.ResponseRequest);
        Assert.Equal(context, probe.CallbackContext);
        if (input.Mode != "request") Assert.Equal(context, probe.LastContext);
        string expectedType = input.Mode == "mutate-request" ? "hook/type" : "input/type";
        if (outgoing is not null)
        {
            Assert.Equal(expectedType, outgoing.TypeUrl);
            Assert.Equal(input.Mode == "mutate-response" ? "from-hook" : "v1", outgoing.VersionInfo);
        }
        return new FetchEngineResult(input, new FetchEngineObservation(error, probe.Events.ToArray(),
            probe.LastRequest?.TypeUrl, probe.ResponseRequest?.TypeUrl, outgoing?.TypeUrl, outgoing?.VersionInfo));
    }

    internal static void Write(IEnumerable<FetchEngineResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fetch-engine-corpus.json"),
        JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

internal sealed record FetchEngineInput(string Id, string Mode, string[] ExpectedEvents, string ExpectedError = "");
internal sealed record FetchEngineObservation(string Error, string[] Events, string? CacheType, string? CallbackType, string? TypeUrl, string? Version);
internal sealed record FetchEngineResult(FetchEngineInput Input, FetchEngineObservation Observed);
