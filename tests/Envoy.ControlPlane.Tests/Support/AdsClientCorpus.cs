using System.Text.Json;
using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Clients;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.Tests.SotwAdsClientTests;

namespace Envoy.ControlPlane.Tests.Support;

internal static class AdsClientCorpus
{
    internal static AdsClientInput[] Cases() =>
    [
        new("before-init", [new("fetch"), new("ack"), new("nack")], [], [new("init"), new("init"), new("init")]),
        new("initial", [new("init")], [new("", "")], []),
        new("ack-flow", [new("init"), new("receive", "v1", "n1", ["a", "b"]), new("ack"), new("receive", "v2", "n2", ["c"]), new("ack")],
            [new("", ""), new("v1", "n1"), new("v2", "n2")], []),
        new("nack-before-acceptance", [new("init"), new("receive", "v1", "n1", ["a"]), new("nack")],
            [new("", ""), new("", "n1", "rejected")], []),
        new("nack-retains-accepted", [new("init"), new("receive", "v1", "n1", ["a"]), new("ack"), new("receive", "v2", "n2", ["b"]), new("nack"), new("nack")],
            [new("", ""), new("v1", "n1"), new("v1", "n2", "rejected"), new("v1", "n2", "rejected")], []),
        new("ack-send-failure", [new("init"), new("receive", "v1", "n1"), new("send-error-ack"), new("nack")],
            [new("", ""), new("v1", "n1"), new("v1", "n1", "rejected")], [new("send")]),
        new("receive-failure-retains-state", [new("init"), new("receive", "v1", "n1"), new("receive-error"), new("ack")],
            [new("", ""), new("v1", "n1")], [new("unavailable", true)]),
        new("reinit-after-nack", [new("init"), new("receive", "v1", "n1"), new("nack"), new("init")],
            [new("", ""), new("", "n1", "rejected"), new("v1", "n1", Call: 2)], []),
        new("open-failure-retains-call", [new("init"), new("receive", "v1", "n1"), new("open-error-init"), new("ack")],
            [new("", ""), new("v1", "n1")], [new("open")]),
        new("initial-send-failure", [new("send-error-init"), new("nack")],
            [new("", ""), new("", "", "rejected")], [new("send")]),
        new("eof", [new("init"), new("eof")], [new("", "")], [new("eof", true)]),
        new("empty-response", [new("init"), new("receive", "empty", "nonce"), new("ack")],
            [new("", ""), new("empty", "nonce")], []),
    ];

    internal static async Task<AdsClientResult> EvaluateAsync(AdsClientInput input)
    {
        var invoker = new ScriptedAdsInvoker();
        using var client = new SotwAdsClient(new Node { Id = "node" }, ResourceTypeUrls.Route);
        var errors = new List<AdsClientError>();
        var resources = new List<string[]>();
        foreach (var step in input.Steps)
        {
            try
            {
                switch (step.Operation)
                {
                    case "init":
                        await client.InitializeAsync(invoker);
                        break;
                    case "open-error-init":
                        invoker.OpenFailure = new IOException("fixture-open");
                        await client.InitializeAsync(invoker);
                        break;
                    case "send-error-init":
                        invoker.NextCall = new ScriptedAdsCall { SendFailure = new IOException("fixture-send") };
                        await client.InitializeAsync(invoker);
                        break;
                    case "receive":
                        invoker.Calls[^1].Enqueue(Response(step.Version, step.Nonce, step.Names ?? []));
                        var received = await client.FetchAsync(TestContext.Current.CancellationToken);
                        Assert.Equal(step.Names ?? [], received.Select(resource => resource.Unpack<RouteConfiguration>().Name));
                        resources.Add(received.Select(resource => Convert.ToBase64String(resource.ToByteArray())).ToArray());
                        break;
                    case "receive-error":
                        invoker.Calls[^1].Enqueue(failure: new RpcException(new Status(StatusCode.Unavailable, "fixture receive failure")));
                        await client.FetchAsync(TestContext.Current.CancellationToken);
                        break;
                    case "eof":
                        invoker.Calls[^1].Enqueue(eof: true);
                        await client.FetchAsync(TestContext.Current.CancellationToken);
                        break;
                    case "fetch":
                        await client.FetchAsync(TestContext.Current.CancellationToken);
                        break;
                    case "send-error-ack":
                        invoker.Calls[^1].SendFailure = new IOException("fixture-send");
                        await client.AckAsync(TestContext.Current.CancellationToken);
                        break;
                    case "ack":
                        await client.AckAsync(TestContext.Current.CancellationToken);
                        break;
                    case "nack":
                        await client.NackAsync("rejected", TestContext.Current.CancellationToken);
                        break;
                    default:
                        throw new ArgumentException("Unknown client fixture operation: " + step.Operation);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or RpcException)
            {
                errors.Add(new AdsClientError(ErrorKind(error), SotwAdsClient.IsConnectionError(error)));
            }
        }
        var requests = invoker.Calls.SelectMany((call, index) => call.Requests.Select(request => Observe(request, index + 1))).ToArray();
        Assert.Equal(input.ExpectedRequests, requests);
        Assert.Equal(input.ExpectedErrors, errors);
        return new AdsClientResult(input, new AdsClientObservation(requests, errors.ToArray(), resources.ToArray()));
    }

    private static AdsClientRequest Observe(DiscoveryRequest request, int call)
    {
        Assert.Equal("node", request.Node.Id);
        Assert.Equal(ResourceTypeUrls.Route, request.TypeUrl);
        Assert.Empty(request.ResourceNames);
        return new AdsClientRequest(request.VersionInfo, request.ResponseNonce, request.ErrorDetail?.Message, call, request.ErrorDetail?.Code ?? 0);
    }

    private static string ErrorKind(Exception error) => error switch
    {
        EndOfStreamException => "eof",
        IOException { Message: "fixture-open" } => "open",
        IOException { Message: "fixture-send" } => "send",
        RpcException { StatusCode: StatusCode.Unavailable } => "unavailable",
        InvalidOperationException { Message: "The ADS client has not been initialized." } => "init",
        _ => throw new InvalidOperationException("Unexpected client fixture failure", error),
    };

    internal static void Write(IEnumerable<AdsClientResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ads-client-corpus.json"),
        JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

internal sealed record AdsClientInput(string Id, AdsClientStep[] Steps, AdsClientRequest[] ExpectedRequests, AdsClientError[] ExpectedErrors);
internal sealed record AdsClientStep(string Operation, string Version = "", string Nonce = "", string[]? Names = null);
internal sealed record AdsClientRequest(string Version, string Nonce, string? Error = null, int Call = 1, int Code = 0);
internal sealed record AdsClientError(string Kind, bool Connection = false);
internal sealed record AdsClientObservation(AdsClientRequest[] Requests, AdsClientError[] Errors, string[][] Resources);
internal sealed record AdsClientResult(AdsClientInput Input, AdsClientObservation Observed);
