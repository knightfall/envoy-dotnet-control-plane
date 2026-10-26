using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class FetchServerTests
{
    [Fact]
    public async Task Callbacks_PreserveOrderIdentityContextAndMutations()
    {
        var request = new DiscoveryRequest();
        var envelope = new DiscoveryResponse();
        var probe = new FetchProbe();
        using var stop = new CancellationTokenSource();
        var context = new ResponseContext(stop.Token, new object());
        probe.BeforeRequest = input => input.TypeUrl = "application/type";
        probe.Produce = input => new TestSotwResponse(input, "v1", new Dictionary<string, string>(), default, () =>
        {
            probe.Events.Add("materialize");
            return envelope;
        });
        probe.BeforeResponse = output =>
        {
            Assert.Same(envelope, output);
            output!.Nonce = "callback";
        };
        var response = await new FetchServer(probe, probe).FetchAsync(request, context);
        Assert.Same(envelope, response);
        Assert.Same(request, probe.LastRequest);
        Assert.NotNull(probe.LastRequest);
        Assert.Equal("application/type", probe.LastRequest.TypeUrl);
        Assert.Equal("callback", response.Nonce);
        Assert.Equal(context, probe.LastContext);
        Assert.Equal(context, probe.CallbackContext);
        Assert.Equal(new[] { "request", "fetch", "materialize", "response" }, probe.Events);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("fetch")]
    [InlineData("materialize")]
    [InlineData("response")]
    public async Task Failures_PreserveOriginalExceptionAndSourceCallbackBoundary(string stage)
    {
        var failure = new InvalidOperationException(stage);
        var probe = new FetchProbe();
        probe.BeforeRequest = _ => { if (stage == "request") throw failure; };
        probe.Produce = request =>
        {
            if (stage == "fetch") throw failure;
            return new TestSotwResponse(request, "v1", new Dictionary<string, string>(), default, () =>
            {
                probe.Events.Add("materialize");
                return stage == "materialize" ? throw failure : new DiscoveryResponse();
            });
        };
        probe.BeforeResponse = response =>
        {
            Assert.Equal(stage == "materialize", response is null);
            if (stage == "response") throw failure;
        };
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => new FetchServer(probe, probe).FetchAsync(new DiscoveryRequest())));
        string[] expected = stage switch
        {
            "request" => ["request"],
            "fetch" => ["request", "fetch"],
            "materialize" => ["request", "fetch", "materialize", "response:null"],
            _ => ["request", "fetch", "materialize", "response"],
        };
        Assert.Equal(expected, probe.Events);
    }

    [Fact]
    public async Task MissingEnvelope_FailsBeforeResponseCallback()
    {
        var probe = new FetchProbe { Produce = _ => null! };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new FetchServer(probe, probe).FetchAsync(new DiscoveryRequest()));
        Assert.Equal("Missing response.", error.Message);
        Assert.Equal(new[] { "request", "fetch" }, probe.Events);
    }

    [Fact]
    public async Task Callbacks_AreOptionalAndSkipPropagatesUnchanged()
    {
        var skip = new SkipFetchException();
        var probe = new FetchProbe { Produce = _ => throw skip };
        Assert.Same(skip, await Assert.ThrowsAsync<SkipFetchException>(() => new FetchServer(probe).FetchAsync(new DiscoveryRequest())));
        Assert.Equal(new[] { "fetch" }, probe.Events);
    }
}
