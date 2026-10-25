using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwResponseTests
{
    [Fact]
    public async Task ImmediateResponse_UpdatesMetadataBeforeCallbackAndRetainsAssignedNonce()
    {
        await using var stream = new StreamHarness();
        var origin = new object();
        using var publication = new CancellationTokenSource();
        TestSotwResponse? response = null;
        stream.Watcher.Create = (request, _, responses) =>
        {
            var watch = new StreamWatch(responses);
            response = Response(request, names: ["a"], context: new ResponseContext(publication.Token, origin));
            watch.Respond(response);
            return watch;
        };
        stream.Callbacks.Response = (context, _, _, outgoing, token) =>
        {
            Assert.Same(origin, context.State);
            Assert.Equal(publication.Token, context.CancellationToken);
            Assert.True(token.CanBeCanceled);
            Assert.Equal("v1", stream.Watcher.Registrations.Last().Subscription.ReturnedResources["a"]);
            Assert.Equal("1", outgoing.Nonce);
            outgoing.Nonce = "callback-nonce";
            return ValueTask.CompletedTask;
        };
        stream.Start();
        stream.Input.Push(Request(names: ["a"]));
        var first = await stream.NextWatch();
        Assert.Equal("callback-nonce", (await stream.NextResponse()).Nonce);
        stream.Watcher.Create = null;
        stream.Input.Push(Request(nonce: "1", names: ["a"]));
        await stream.NextWatch();
        Assert.Equal(1, first.Watch.Disposals);
        Assert.Equal(1, response!.Materializations);
        await stream.Finish();
        Assert.Equal(2, stream.Watcher.Registrations.Count);
        Assert.False(stream.Sent.Reader.TryRead(out _));
    }

    [Fact]
    public async Task UnsubscribedResponse_IsConsumedWithoutMaterializingOrSending()
    {
        await using var stream = new StreamHarness();
        stream.Start();
        stream.Input.Push(Request(names: ["wanted"]));
        var watch = await stream.NextWatch();
        var inspected = Signal();
        var response = Response(watch.Request, names: ["unwanted"]);
        watch.Watch.Respond(new InspectedResponse(response, inspected));
        await Wait(inspected.Task);
        stream.Input.Push(Request(type: "barrier"));
        await stream.NextWatch();
        Assert.Equal(0, response.Materializations);
        Assert.Empty(watch.Subscription.ReturnedResources);
        Assert.False(stream.Sent.Reader.TryRead(out _));
        await stream.Finish();
    }

    [Fact]
    public async Task SharedResponse_ClonesEnvelopeAndPreservesPayloadAndUnknownFieldsAcrossStreams()
    {
        var body = Envoy.Service.Discovery.V3.DiscoveryResponse.Parser.ParseFrom([0x98, 0x06, 0x07]);
        body.VersionInfo = "shared";
        body.Resources.Add(new Any { TypeUrl = "type/payload", Value = ByteString.CopyFromUtf8("stable") });
        await using var first = new StreamHarness();
        await using var second = new StreamHarness();
        first.Callbacks.Response = (_, _, _, outgoing, _) =>
        {
            outgoing.Resources[0].TypeUrl = "callback/first";
            outgoing.ControlPlane = new Envoy.Config.Core.V3.ControlPlane { Identifier = "first" };
            return ValueTask.CompletedTask;
        };
        first.Start();
        second.Start();
        first.Input.Push(Request());
        var watchA = await first.NextWatch();
        watchA.Watch.Respond(Response(watchA.Request, body: body));
        var outgoingA = await first.NextResponse();
        second.Input.Push(Request());
        var watchB = await second.NextWatch();
        watchB.Watch.Respond(Response(watchB.Request, body: body));
        var outgoingB = await second.NextResponse();
        Assert.Equal("callback/first", outgoingA.Resources[0].TypeUrl);
        Assert.Equal("type/payload", outgoingB.Resources[0].TypeUrl);
        Assert.Null(outgoingB.ControlPlane);
        Assert.Equal("", body.Nonce);
        Assert.Equal("1", outgoingA.Nonce);
        Assert.Equal("1", outgoingB.Nonce);
        Assert.Same(body.Resources[0].Value, outgoingB.Resources[0].Value);
        outgoingB.Nonce = "";
        Assert.Equal(body.ToByteArray(), outgoingB.ToByteArray());
        await first.Finish();
        await second.Finish();
    }

    [Fact]
    public async Task Nack_RedeliversCurrentSnapshotWithoutRollbackAndCallbacksCanPublish()
    {
        var cache = new SnapshotCache();
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Cluster, [ResourceFixtures.EdsCluster("a")]));
        await cache.SetSnapshotAsync("client", snapshot);
        await using var stream = new StreamHarness();
        int nacks = 0;
        stream.Callbacks.Request = (_, request, _) =>
        {
            if (request.ErrorDetail is not null)
            {
                Assert.Equal("rejected", request.ErrorDetail.Message);
                nacks++;
            }
            return ValueTask.CompletedTask;
        };
        stream.Callbacks.Response = async (_, _, _, _, token) =>
            await Task.Run(async () => await cache.SetSnapshotAsync("client", snapshot), token);
        stream.Start(ResourceTypeUrls.Cluster, cache);
        var first = Request(type: "", names: ["a"]);
        first.Node = new Node { Id = "client" };
        stream.Input.Push(first);
        Assert.Equal("v1", (await stream.NextResponse()).VersionInfo);
        var nack = Request(type: "", nonce: "1", names: ["a"]);
        nack.ErrorDetail = new Google.Rpc.Status { Code = 3, Message = "rejected" };
        stream.Input.Push(nack);
        var repeated = await stream.NextResponse();
        Assert.Equal("v1", repeated.VersionInfo);
        Assert.Equal("2", repeated.Nonce);
        Assert.Same(snapshot, cache.GetSnapshot("client"));
        Assert.Equal(1, nacks);
        await stream.Finish();
        Assert.Equal(0, cache.GetStatusInfo("client")!.NumWatches);
    }

}
