using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests.Support;

internal static class SotwStreamCorpus
{
    internal static JsonArray ReadFixtures()
    {
        using var source = typeof(SotwStreamCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.StreamCases.json")!;
        return JsonNode.Parse(source)!.AsArray();
    }

    internal static async Task<JsonObject> EvaluateAsync(JsonNode fixture)
    {
        await using var stream = new StreamHarness();
        var events = new ConcurrentQueue<string>();
        var requested = Channel.CreateUnbounded<bool>();
        DiscoveryRequest? heldRequest = null;
        DiscoveryRequest? rejectedRequest = null;
        var release = Signal();
        stream.Callbacks.Open = (_, _, type, _) =>
        {
            events.Enqueue("open|" + type);
            if (fixture["rejectOpen"]?.GetValue<bool>() == true)
            {
                throw new SotwProtocolException(SotwProtocolError.InvalidArgument, "callback rejected");
            }
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Request = async (_, request, token) =>
        {
            events.Enqueue($"request|{request.TypeUrl}|{request.Node.Id}|{request.VersionInfo}|{request.ResponseNonce}|{string.Join(',', request.ResourceNames)}|{(request.ErrorDetail is not null).ToString().ToLowerInvariant()}");
            Assert.True(requested.Writer.TryWrite(true));
            if (ReferenceEquals(request, rejectedRequest))
            {
                throw new SotwProtocolException(SotwProtocolError.InvalidArgument, "callback rejected");
            }
            if (ReferenceEquals(request, heldRequest)) await release.Task.WaitAsync(token);
        };
        stream.Watcher.Create = (request, subscription, responses) =>
        {
            string returned = string.Join(',', subscription.ReturnedResources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
            events.Enqueue($"watch|{request.TypeUrl}|{request.VersionInfo}|{string.Join(',', request.ResourceNames)}|{returned}|{subscription.IsWildcard.ToString().ToLowerInvariant()}");
            return new StreamWatch(responses);
        };
        stream.Callbacks.Response = (_, _, _, response, _) =>
        {
            events.Enqueue($"response|{response.TypeUrl}|{response.VersionInfo}|{response.Nonce}");
            if (fixture["callbackNonce"] is { } value)
            {
                response.Nonce = value.GetValue<string>();
            }
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Close = (_, node) => { events.Enqueue("close|" + node.Id); return ValueTask.CompletedTask; };
        stream.Sending = (response, _) =>
        {
            events.Enqueue($"send|{response.TypeUrl}|{response.VersionInfo}|{response.Nonce}");
            return ValueTask.CompletedTask;
        };
        stream.Start(fixture["defaultType"]!.GetValue<string>(), options: new SotwOptions
        {
            OrderedAds = fixture["ordered"]?.GetValue<bool>() == true,
        });
        var watches = new List<StreamRegistration>();
        string? terminalError = null;
        foreach (var step in fixture["steps"]!.AsArray())
        {
            switch (step!["kind"]!.GetValue<string>())
            {
                case "request":
                case "request-error":
                    var request = Request(step["type"]?.GetValue<string>() ?? "", step["nonce"]?.GetValue<string>() ?? "", Strings(step["names"]));
                    request.VersionInfo = step["version"]?.GetValue<string>() ?? "";
                    if (step["node"] is { } node) request.Node = new Node { Id = node.GetValue<string>() };
                    if (step["nack"]?.GetValue<bool>() == true) request.ErrorDetail = new Google.Rpc.Status { Code = 3, Message = "rejected" };
                    if (step["hold"]?.GetValue<bool>() == true) heldRequest = request;
                    if (step["reject"]?.GetValue<bool>() == true) rejectedRequest = request;
                    stream.Input.Push(request);
                    if (step["kind"]!.GetValue<string>() == "request-error")
                    {
                        terminalError = (await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running))).Error.ToString();
                        break;
                    }
                    await Wait(requested.Reader.ReadAsync().AsTask());
                    if (step["stale"]?.GetValue<bool>() != true && step["hold"]?.GetValue<bool>() != true) watches.Add(await stream.NextWatch());
                    break;
                case "respond":
                case "queue":
                    var registration = watches[step["watch"]!.GetValue<int>()];
                    registration.Watch.Respond(Response(registration.Request, step["version"]!.GetValue<string>(), Strings(step["names"])));
                    if (step["kind"]!.GetValue<string>() == "respond") await stream.NextResponse();
                    break;
                case "release":
                    release.TrySetResult();
                    if (step["stale"]?.GetValue<bool>() != true) watches.Add(await stream.NextWatch());
                    for (int count = 0; count < step["sent"]!.GetValue<int>(); count++) await stream.NextResponse();
                    break;
                case "close-responses":
                    watches[step["watch"]!.GetValue<int>()].Watch.CompleteResponses();
                    var failure = await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running));
                    terminalError = failure.Error.ToString();
                    break;
                case "error":
                    terminalError = (await Assert.ThrowsAsync<SotwProtocolException>(() => Wait(stream.Running))).Error.ToString();
                    break;
                case "end":
                    stream.Input.Complete(step["receiveError"]?.GetValue<bool>() == true ? new IOException("receive") : null);
                    await Wait(stream.Running);
                    break;
                default:
                    throw new InvalidOperationException("Unknown stream corpus step");
            }
        }
        var observed = JsonSerializer.SerializeToNode(new
        {
            events = events.ToArray(),
            disposals = watches.Select(registration => registration.Watch.Disposals).ToArray(),
        })!.AsObject();
        if (terminalError is not null) observed["error"] = terminalError;
        return observed;
    }

    private static string[] Strings(JsonNode? values) => values?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];
}
