using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class SnapshotPublicationTransportTests
{
    [Fact]
    public async Task FullOrderedAdsQueue_DoesNotDelayAnotherNodesGrpcResponse()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        await using var host = await StartAsync(new SotwOptions { OrderedAds = true },
            configure: (_, callbacks) => callbacks.ResponseHandler = async (response, cancellation) =>
            {
                if (response.VersionInfo == "slow-v1")
                {
                    entered.TrySetResult();
                    await resume.Task.WaitAsync(cancellation);
                }
            });
        var client = new AggregatedDiscoveryService.AggregatedDiscoveryServiceClient(host.Channel);
        using var slow = client.StreamAggregatedResources(cancellationToken: token);
        using var healthy = client.StreamAggregatedResources(cancellationToken: token);
        const int extraTypes = 3;
        string[] types = Enumerable.Range(0, (int)ResponseType.Unknown + extraTypes)
            .Select(index => $"type.googleapis.com/test.ads.Resource{index}").ToArray();
        foreach (string type in types)
        {
            await slow.RequestStream.WriteAsync(new DiscoveryRequest { TypeUrl = type, Node = new Node { Id = "slow" } }, token);
            var created = await Wait(host.Cache.Created.Reader.ReadAsync(token).AsTask());
            Assert.Equal(type, created.TypeUrl);
        }
        await healthy.RequestStream.WriteAsync(new DiscoveryRequest
        {
            TypeUrl = ResourceTypeUrls.Cluster, Node = new Node { Id = "healthy" },
        }, token);
        Assert.Equal("healthy", (await Wait(host.Cache.Created.Reader.ReadAsync(token).AsTask())).Node.Id);
        var snapshot = new Snapshot("healthy-v1", new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Cluster] = [new Cluster { Name = "healthy-cluster" }],
        });

        var blocked = host.Cache.Inner.SetSnapshotAsync("slow", new MultiTypeSnapshot(types));
        try
        {
            await Wait(entered.Task);
            Assert.False(blocked.IsCompleted);
            await Wait(host.Cache.Inner.SetSnapshotAsync("healthy", snapshot));
            Assert.True(await Wait(healthy.ResponseStream.MoveNext(token)));
            Assert.Equal("healthy-v1", healthy.ResponseStream.Current.VersionInfo);
            Assert.Equal("healthy-cluster", Assert.Single(healthy.ResponseStream.Current.Resources).Unpack<Cluster>().Name);
            Assert.False(blocked.IsCompleted);
        }
        finally
        {
            resume.TrySetResult();
        }

        foreach (string type in types)
        {
            Assert.True(await Wait(slow.ResponseStream.MoveNext(token)));
            Assert.Equal(type, slow.ResponseStream.Current.TypeUrl);
            Assert.Equal("slow-v1", slow.ResponseStream.Current.VersionInfo);
        }
        await Wait(blocked);
    }

    private sealed class MultiTypeSnapshot(IEnumerable<string> types) : IResourceSnapshot
    {
        private readonly HashSet<string> known = new(types, StringComparer.Ordinal);
        private readonly ResourceCollection resources = new("slow-v1", [new StringValue { Value = "slow" }]);

        public string GetVersion(string typeUrl) => known.Contains(typeUrl) ? resources.Version : "";
        public IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl) =>
            known.Contains(typeUrl) ? resources.Items : null;
        public IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl) => GetResourcesAndTtl(typeUrl)?
            .ToDictionary(pair => pair.Key, pair => pair.Value.Resource, StringComparer.Ordinal);
        public void ConstructVersionMap() { }
        public IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl) => null;
    }
}
