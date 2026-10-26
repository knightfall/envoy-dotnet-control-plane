using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Clients;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwAdsClientTests
{
    [Fact]
    public async Task SourceCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = AdsClientCorpus.Cases();
        Assert.Equal(12, inputs.Length);
        Assert.Equal(12, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<AdsClientResult>();
        foreach (var input in inputs) results.Add(await AdsClientCorpus.EvaluateAsync(input));
        AdsClientCorpus.Write(results);
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("ack")]
    [InlineData("nack")]
    public async Task BeforeInitialization_RequiresAnExplicitConnection(string operation)
    {
        using var client = Client();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(client, operation));
        Assert.Equal("The ADS client has not been initialized.", error.Message);
        Assert.False(SotwAdsClient.IsConnectionError(error));
    }

    [Fact]
    public async Task Initialize_UsesGeneratedAdsMethodAndCallerOptions()
    {
        var node = new Node { Id = "node", Cluster = "control" };
        var invoker = new ScriptedAdsInvoker();
        using var client = new SotwAdsClient(node, ResourceTypeUrls.Route);
        using var stop = new CancellationTokenSource();
        var headers = new Grpc.Core.Metadata { { "x-tenant", "application" } };
        var options = new CallOptions(headers, DateTime.UtcNow.AddMinutes(1), stop.Token);
        await client.InitializeAsync(invoker, options);
        var call = Assert.Single(invoker.Calls);
        var request = Assert.Single(call.Requests);
        Assert.Equal("/envoy.service.discovery.v3.AggregatedDiscoveryService/StreamAggregatedResources", invoker.Method);
        Assert.Same(headers, invoker.Options.Headers);
        Assert.Equal(options.Deadline, invoker.Options.Deadline);
        Assert.Equal(stop.Token, invoker.Options.CancellationToken);
        Assert.Equal(node, request.Node);
        Assert.Equal(ResourceTypeUrls.Route, request.TypeUrl);
        Assert.Empty(request.VersionInfo);
        Assert.Empty(request.ResponseNonce);
        Assert.Empty(request.ResourceNames);
        Assert.Null(request.ErrorDetail);
        Assert.Equal(0, call.Reads);
    }

    [Fact]
    public async Task ReceiveAckAndNack_TrackAcceptedVersionAndLatestNonce()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = Client();
        await client.InitializeAsync(invoker);
        var call = Assert.Single(invoker.Calls);
        var first = Response("v1", "nonce-1", "a", "b");
        call.Enqueue(first);
        Assert.Same(first.Resources, await client.FetchAsync(TestContext.Current.CancellationToken));
        await client.AckAsync(TestContext.Current.CancellationToken);
        var second = Response("v2", "nonce-2", "changed");
        call.Enqueue(second);
        Assert.Equal("changed", Assert.Single(await client.FetchAsync(TestContext.Current.CancellationToken)).Unpack<RouteConfiguration>().Name);
        await client.NackAsync("invalid route", TestContext.Current.CancellationToken);
        await client.NackAsync("still invalid", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "", "v1", "v1", "v1" }, call.Requests.Select(request => request.VersionInfo));
        Assert.Equal(new[] { "", "nonce-1", "nonce-2", "nonce-2" }, call.Requests.Select(request => request.ResponseNonce));
        var rejected = call.Requests[2];
        Assert.NotNull(rejected.ErrorDetail);
        Assert.Equal("invalid route", rejected.ErrorDetail.Message);
        Assert.Equal(0, rejected.ErrorDetail.Code);
        Assert.Equal("still invalid", call.Requests[3].ErrorDetail.Message);
        await client.AckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v2", call.Requests[^1].VersionInfo);
        Assert.Equal("nonce-2", call.Requests[^1].ResponseNonce);
        Assert.Null(call.Requests[^1].ErrorDetail);
    }

    [Fact]
    public async Task SendFailure_StillAdvancesAcceptanceBeforeTheNextNack()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = Client();
        await client.InitializeAsync(invoker);
        var call = Assert.Single(invoker.Calls);
        call.Enqueue(Response("accepted-before-send", "received"));
        Assert.Empty(await client.FetchAsync(TestContext.Current.CancellationToken));
        var failure = new IOException("send failed");
        call.SendFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => client.AckAsync(TestContext.Current.CancellationToken)));
        await client.NackAsync("rejected after send failure", TestContext.Current.CancellationToken);
        Assert.Equal("accepted-before-send", call.Requests[^1].VersionInfo);
        Assert.Equal("received", call.Requests[^1].ResponseNonce);
        Assert.False(SotwAdsClient.IsConnectionError(failure));
    }

    [Fact]
    public async Task ReceiveFailureAndNullResponse_KeepThePreviousReceivedState()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = Client();
        await client.InitializeAsync(invoker);
        var call = Assert.Single(invoker.Calls);
        call.Enqueue(Response("v1", "previous"));
        await client.FetchAsync(TestContext.Current.CancellationToken);
        var failure = new RpcException(new Status(StatusCode.Unavailable, "receive failure"));
        call.Enqueue(failure: failure);
        Assert.Same(failure, await Assert.ThrowsAsync<RpcException>(() => client.FetchAsync(TestContext.Current.CancellationToken)));
        call.Enqueue();
        Assert.Equal("The ADS server returned a null response.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchAsync(TestContext.Current.CancellationToken))).Message);
        await client.AckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v1", call.Requests[^1].VersionInfo);
        Assert.Equal("previous", call.Requests[^1].ResponseNonce);
        Assert.Single(invoker.Calls);
    }

    [Fact]
    public async Task Reinitialization_RetainsSourceStateAndDisposesOnlyTheOldCall()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = Client();
        await client.InitializeAsync(invoker);
        var first = Assert.Single(invoker.Calls);
        first.Enqueue(Response("previously-nacked", "latest"));
        await client.FetchAsync(TestContext.Current.CancellationToken);
        await client.NackAsync("rejected", TestContext.Current.CancellationToken);
        Assert.Empty(first.Requests[^1].VersionInfo);
        await client.InitializeAsync(invoker);
        Assert.Equal(2, invoker.Calls.Count);
        Assert.Equal(1, first.Disposals);
        var next = invoker.Calls[1];
        var initial = Assert.Single(next.Requests);
        Assert.Equal("previously-nacked", initial.VersionInfo);
        Assert.Equal("latest", initial.ResponseNonce);
        Assert.Null(initial.ErrorDetail);
        Assert.Equal(0, next.Disposals);
        client.Dispose();
        client.Dispose();
        Assert.Equal(1, next.Disposals);
        Assert.Equal(1, first.Disposals);
    }

    [Fact]
    public async Task FailedOpen_KeepsTheOldCall_AndInitialSendFailureKeepsTheNewCall()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = Client();
        await client.InitializeAsync(invoker);
        var old = Assert.Single(invoker.Calls);
        var openFailure = new IOException("open failed");
        invoker.OpenFailure = openFailure;
        Assert.Same(openFailure, await Assert.ThrowsAsync<IOException>(() => client.InitializeAsync(invoker)));
        Assert.Equal(0, old.Disposals);
        await client.AckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, old.Requests.Count);
        var sendFailure = new IOException("initial send failed");
        var replacement = new ScriptedAdsCall { SendFailure = sendFailure };
        invoker.NextCall = replacement;
        Assert.Same(sendFailure, await Assert.ThrowsAsync<IOException>(() => client.InitializeAsync(invoker)));
        Assert.Equal(1, old.Disposals);
        Assert.Equal(0, replacement.Disposals);
        await client.NackAsync("still using the replacement", TestContext.Current.CancellationToken);
        Assert.Equal(2, replacement.Requests.Count);
        Assert.Equal(2, invoker.Calls.Count);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable, true)]
    [InlineData(StatusCode.Cancelled, true)]
    [InlineData(StatusCode.DeadlineExceeded, false)]
    [InlineData(StatusCode.Unknown, false)]
    [InlineData(StatusCode.PermissionDenied, false)]
    public void ConnectionErrorClassification_UsesTheSourceGrpcCodes(StatusCode code, bool expected)
    {
        var error = new RpcException(new Status(code, "fixture"));
        Assert.Equal(expected, SotwAdsClient.IsConnectionError(error));
        Assert.Equal(expected, SotwAdsClient.IsConnectionError(new IOException("wrapped", error)));
    }

    [Fact]
    public async Task Eof_IsClassifiedWithoutReconnecting()
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = Client();
        await client.InitializeAsync(invoker);
        Assert.Single(invoker.Calls).Enqueue(eof: true);
        var error = await Assert.ThrowsAsync<EndOfStreamException>(() => client.FetchAsync(TestContext.Current.CancellationToken));
        Assert.True(SotwAdsClient.IsConnectionError(error));
        Assert.True(SotwAdsClient.IsConnectionError(new IOException("wrapped", error)));
        Assert.False(SotwAdsClient.IsConnectionError(null));
        Assert.False(SotwAdsClient.IsConnectionError(new OperationCanceledException()));
        Assert.Single(invoker.Calls);
    }

    private static SotwAdsClient Client() => new(new Node { Id = "node" }, ResourceTypeUrls.Route);

    internal static DiscoveryResponse Response(string version, string nonce, params string[] names)
    {
        var response = new DiscoveryResponse { VersionInfo = version, Nonce = nonce, TypeUrl = ResourceTypeUrls.Route };
        response.Resources.Add(names.Select(name => Any.Pack(new RouteConfiguration { Name = name })));
        return response;
    }

    private static async Task InvokeAsync(SotwAdsClient client, string operation)
    {
        if (operation == "fetch") await client.FetchAsync(TestContext.Current.CancellationToken);
        else if (operation == "ack") await client.AckAsync(TestContext.Current.CancellationToken);
        else await client.NackAsync("rejected", TestContext.Current.CancellationToken);
    }
}
