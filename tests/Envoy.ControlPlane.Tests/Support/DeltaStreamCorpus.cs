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

internal static class DeltaStreamCorpus
{
    internal static JsonArray ReadFixtures()
    {
        using var source = typeof(DeltaStreamCorpus).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Tests.DeltaStreamCases.json")!;
        return JsonNode.Parse(source)!.AsArray();
    }

    internal static async Task<JsonObject> EvaluateAsync(JsonNode fixture)
    {
        await using var stream = new DeltaHarness();
        var events = new ConcurrentQueue<string>();
        var requested = Channel.CreateUnbounded<bool>();
        var ownedWatches = new List<DeltaRegistration>();
        DeltaDiscoveryRequest? held = null;
        DeltaDiscoveryRequest? rejected = null;
        var release = Signal();
        stream.Callbacks.Open = (_, _, type, _) =>
        {
            events.Enqueue("open|" + type);
            if (fixture["rejectOpen"]?.GetValue<bool>() == true)
            {
                throw new DeltaProtocolException(DeltaProtocolError.InvalidArgument, "callback rejected");
            }
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Request = async (_, request, token) =>
        {
            events.Enqueue($"request|{request.TypeUrl}|{request.Node?.Id}|{request.ResponseNonce}|{string.Join(',', request.ResourceNamesSubscribe)}|{string.Join(',', request.ResourceNamesUnsubscribe)}|{(request.ErrorDetail is not null).ToString().ToLowerInvariant()}");
            Assert.True(requested.Writer.TryWrite(true));
            if (ReferenceEquals(request, rejected))
            {
                throw new DeltaProtocolException(DeltaProtocolError.InvalidArgument, "callback rejected");
            }
            if (request.TypeUrl == "mutate" && fixture["mutate"]?.GetValue<bool>() == true)
            {
                request.TypeUrl = "changed";
                request.Node = new Node { Id = "callback" };
                request.ResourceNamesSubscribe.Add("selected");
            }
            if (ReferenceEquals(request, held))
            {
                await release.Task.WaitAsync(token);
            }
        };
        stream.Watcher.Create = (request, subscription, writer) =>
        {
            string returned = string.Join(',', subscription.ReturnedResources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
            events.Enqueue($"watch|{request.TypeUrl}|{string.Join(',', subscription.SubscribedResources.Order(StringComparer.Ordinal))}|{string.Join(',', subscription.SubscribedPrefixes.Order(StringComparer.Ordinal))}|{returned}|{subscription.IsWildcard.ToString().ToLowerInvariant()}");
            var registration = new DeltaRegistration(request, subscription, writer);
            ownedWatches.Add(registration);
            return registration;
        };
        stream.Callbacks.Response = (_, _, response, _) =>
        {
            events.Enqueue($"response|{response.TypeUrl}|{response.SystemVersionInfo}|{response.Nonce}");
            if (fixture["callbackNonce"] is { } value)
            {
                response.Nonce = value.GetValue<string>();
            }
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Close = (_, node) => { events.Enqueue("close|" + node.Id); return ValueTask.CompletedTask; };
        stream.Sending = (response, _) =>
        {
            events.Enqueue($"send|{response.TypeUrl}|{response.SystemVersionInfo}|{response.Nonce}");
            return ValueTask.CompletedTask;
        };
        stream.Start(fixture["defaultType"]!.GetValue<string>(), options: new DeltaOptions
        {
            DisableLegacyWildcard = fixture["disableLegacy"]?.GetValue<bool>() == true,
            LegacyWildcardDisabledTypes = Strings(fixture["disabledTypes"]).ToHashSet(StringComparer.Ordinal),
        });
        var watches = new List<DeltaRegistration>();
        string? terminalError = null;
        foreach (var step in fixture["steps"]!.AsArray())
        {
            switch (step!["kind"]!.GetValue<string>())
            {
                case "request":
                case "request-error":
                    var request = DeltaStreamFixtures.Request(step["type"]?.GetValue<string>() ?? "", step["nonce"]?.GetValue<string>() ?? "",
                        Strings(step["add"]), Strings(step["remove"]));
                    if (step["node"] is { } node)
                    {
                        request.Node = new Node { Id = node.GetValue<string>() };
                    }
                    foreach (var pair in step["initial"]?.AsObject() ?? [])
                    {
                        request.InitialResourceVersions.Add(pair.Key, pair.Value!.GetValue<string>());
                    }
                    if (step["nack"]?.GetValue<bool>() == true)
                    {
                        request.ErrorDetail = new Google.Rpc.Status { Code = 3, Message = "rejected" };
                    }
                    if (step["hold"]?.GetValue<bool>() == true)
                    {
                        held = request;
                    }
                    if (step["reject"]?.GetValue<bool>() == true)
                    {
                        rejected = request;
                    }
                    stream.Input.Push(request);
                    if (step["kind"]!.GetValue<string>() == "request-error")
                    {
                        terminalError = (await Assert.ThrowsAsync<DeltaProtocolException>(() => Wait(stream.Running))).Error.ToString();
                        break;
                    }
                    await Wait(requested.Reader.ReadAsync().AsTask());
                    if (step["hold"]?.GetValue<bool>() != true)
                    {
                        watches.Add(await stream.NextWatch());
                    }
                    break;
                case "respond":
                case "queue":
                    var registration = watches[step["watch"]!.GetValue<int>()];
                    registration.Respond(DeltaStreamFixtures.Response(registration.Request, step["version"]!.GetValue<string>(), Strings(step["names"])));
                    if (step["kind"]!.GetValue<string>() == "respond")
                    {
                        await stream.NextResponse();
                    }
                    break;
                case "release":
                    release.SetResult();
                    watches.Add(await stream.NextWatch());
                    for (int count = 0; count < step["sent"]!.GetValue<int>(); count++)
                    {
                        await stream.NextResponse();
                    }
                    break;
                case "error":
                    terminalError = (await Assert.ThrowsAsync<DeltaProtocolException>(() => Wait(stream.Running))).Error.ToString();
                    break;
                case "end":
                    stream.Input.Complete(step["receiveError"]?.GetValue<bool>() == true ? new IOException("receive") : null);
                    await Wait(stream.Running);
                    break;
                default:
                    throw new InvalidOperationException("Unknown Delta stream corpus step");
            }
        }
        var observed = JsonSerializer.SerializeToNode(new
        {
            events = events.ToArray(),
            disposals = ownedWatches.Select(watch => watch.Disposals).ToArray(),
        })!.AsObject();
        if (terminalError is not null)
        {
            observed["error"] = terminalError;
        }
        return observed;
    }

    private static string[] Strings(JsonNode? values) => values?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];
}
