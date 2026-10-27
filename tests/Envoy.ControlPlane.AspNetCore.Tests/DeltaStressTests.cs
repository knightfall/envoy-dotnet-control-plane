using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Envoy.ControlPlane.AspNetCore.Tests.Support;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Grpc.Core;
using Xunit;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.DeltaDiscoveryCalls;
using static Envoy.ControlPlane.AspNetCore.Tests.Support.GrpcHost;

namespace Envoy.ControlPlane.AspNetCore.Tests;

public sealed class DeltaStressTests
{
    private const int Seed = 43623;
    private const int Rounds = 16;
    private const int NodeCount = 4;
    private static readonly string[] Types = [ResourceTypeUrls.Cluster, ResourceTypeUrls.Route];

    [Fact]
    public async Task RepeatedAdsRounds_UpdateRemoveAndReleasePendingWatchesWithRecordedMemory()
    {
        await using var host = await StartAsync();
        var random = new Random(Seed);
        var samples = new List<MemorySample>();
        var elapsed = Stopwatch.StartNew();
        int canceled = 0;
        for (int round = 0; round < Rounds; round++)
        {
            var plans = Enumerable.Range(0, NodeCount).Select(index => new NodePlan("node-" + index,
                index == 0 || (index > 1 && random.Next(2) == 0),
                index == 1 || (index > 1 && random.Next(2) == 0))).ToArray();
            canceled += plans.Count(plan => plan.Cancel);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var initialReady = Signal();
            var updateReady = Signal();
            var finishReady = Signal();
            var workers = Task.WhenAll(plans.Select(plan => RunNodeAsync(host, plan, round,
                initialReady.Task, updateReady.Task, finishReady.Task, cancellation.Token)));
            try
            {
                await ReadRegistrationsAsync(host, plans);
                Assert.Equal(plans.Count(plan => !plan.Immediate) * Types.Length, PendingWatches(host));
                initialReady.SetResult();
                await ReadRegistrationsAsync(host, plans);
                Assert.Equal(NodeCount * Types.Length, PendingWatches(host));
                updateReady.SetResult();
                await ReadRegistrationsAsync(host, plans);
                Assert.Equal(NodeCount * Types.Length, PendingWatches(host));
                finishReady.SetResult();
                await Wait(workers);
            }
            catch
            {
                await cancellation.CancelAsync();
                try { await Wait(workers); }
                catch (Exception cleanup) { TestContext.Current.TestOutputHelper?.WriteLine("Delta stress worker cleanup: " + cleanup.Message); }
                throw;
            }
            var closed = new List<string>();
            for (int index = 0; index < NodeCount; index++)
            {
                closed.Add((await Wait(host.Callbacks.Closed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask())).Id);
            }
            Assert.Equal(plans.Select(plan => plan.Node).Order(StringComparer.Ordinal), closed.Order(StringComparer.Ordinal));
            Assert.Equal(0, PendingWatches(host));
            Assert.Equal(NodeCount, Drain(host.Callbacks.Opened.Reader).Count);
            Assert.Equal(NodeCount * Types.Length * 3, Drain(host.Callbacks.DeltaRequested.Reader).Count);
            Assert.Equal(NodeCount * Types.Length * 2, Drain(host.Callbacks.DeltaResponses.Reader).Count);
            var ids = new List<long>();
            while (host.Callbacks.StreamIds.TryDequeue(out long id)) ids.Add(id);
            Assert.Equal(Enumerable.Range((round * NodeCount) + 1, NodeCount).Select(value => (long)value), ids.Order());
            Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
            Assert.False(host.Cache.DeltaCreated.Reader.TryRead(out _));
            foreach (var plan in plans) host.Cache.Inner.ClearSnapshot(plan.Node);
            Assert.Empty(host.Cache.Inner.GetStatusKeys());
            using var process = Process.GetCurrentProcess();
            samples.Add(new MemorySample(round, PendingWatches(host), GC.GetTotalMemory(false), process.WorkingSet64));
        }
        await Wait(host.Application.StopAsync(TestContext.Current.CancellationToken));
        Assert.False(host.Callbacks.Closed.Reader.TryRead(out _));
        Assert.Equal(0, host.Cache.Disposals);
        Assert.InRange(canceled, 1, (Rounds * NodeCount) - 1);
        var report = new
        {
            seed = Seed, rounds = Rounds, nodesPerRound = NodeCount,
            streams = Rounds * NodeCount, responses = Rounds * NodeCount * Types.Length * 2,
            canceledStreams = canceled, completedStreams = (Rounds * NodeCount) - canceled,
            elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            platform = RuntimeInformation.OSDescription, framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            memoryScope = "process-wide diagnostics; no retained-memory threshold or throughput SLA",
            samples,
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "delta-stress.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static async Task RunNodeAsync(GrpcHost host, NodePlan plan, int round,
        Task initialReady, Task updateReady, Task finishReady, CancellationToken cancellationToken)
    {
        string initialVersion = $"round-{round}/initial";
        string nextVersion = $"round-{round}/next";
        using var callCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (plan.Immediate) await host.Cache.Inner.SetSnapshotAsync(plan.Node, SnapshotForTypes(initialVersion, "a", "b"));
        using var call = Open(host.Channel, "", callCancellation.Token);
        foreach (string type in Types)
        {
            await call.RequestStream.WriteAsync(NodeRequest(plan.Node, type), cancellationToken);
        }
        await initialReady.WaitAsync(cancellationToken);
        if (!plan.Immediate) await host.Cache.Inner.SetSnapshotAsync(plan.Node, SnapshotForTypes(initialVersion, "a", "b"));
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        var first = await ReadVersionAsync(call, initialVersion, ["a", "b"], [], nonces, cancellationToken);
        foreach (string type in Types)
        {
            await call.RequestStream.WriteAsync(NodeRequest(plan.Node, type, first[type]), cancellationToken);
        }
        await updateReady.WaitAsync(cancellationToken);
        await host.Cache.Inner.SetSnapshotAsync(plan.Node, SnapshotForTypes(nextVersion, "b", "c"));
        var updated = await ReadVersionAsync(call, nextVersion, ["c"], ["a"], nonces, cancellationToken);
        Assert.Equal(Types.Length * 2, nonces.Count);
        foreach (string type in Types)
        {
            await call.RequestStream.WriteAsync(NodeRequest(plan.Node, type, updated[type]), cancellationToken);
        }
        await finishReady.WaitAsync(cancellationToken);
        if (plan.Cancel)
        {
            await callCancellation.CancelAsync();
            var failure = await Assert.ThrowsAsync<RpcException>(() => Wait(call.ResponseStream.MoveNext(cancellationToken)));
            Assert.Equal(StatusCode.Cancelled, failure.StatusCode);
        }
        else
        {
            await call.RequestStream.CompleteAsync();
            Assert.False(await Wait(call.ResponseStream.MoveNext(cancellationToken)));
        }
    }

    private static async Task<Dictionary<string, string>> ReadVersionAsync(
        AsyncDuplexStreamingCall<DeltaDiscoveryRequest, DeltaDiscoveryResponse> call, string version,
        string[] added, string[] removed, HashSet<string> nonces, CancellationToken cancellationToken)
    {
        var received = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < Types.Length; index++)
        {
            Assert.True(await Wait(call.ResponseStream.MoveNext(cancellationToken)));
            var response = call.ResponseStream.Current;
            Assert.Contains(response.TypeUrl, Types);
            Assert.Equal(version, response.SystemVersionInfo);
            Assert.Equal(added, response.Resources.Select(resource => resource.Name).Order(StringComparer.Ordinal));
            Assert.Equal(removed, response.RemovedResources.Order(StringComparer.Ordinal));
            foreach (var resource in response.Resources)
            {
                Assert.NotEmpty(resource.Version);
                Assert.Equal(response.TypeUrl, resource.Resource_.TypeUrl);
                Assert.Equal(Message(response.TypeUrl, resource.Name).ToByteArray(), resource.Resource_.Value.ToByteArray());
            }
            Assert.NotEmpty(response.Nonce);
            Assert.True(nonces.Add(response.Nonce));
            Assert.True(received.TryAdd(response.TypeUrl, response.Nonce));
        }
        return received;
    }

    private static async Task ReadRegistrationsAsync(GrpcHost host, NodePlan[] plans)
    {
        var registrations = new HashSet<(string Node, string Type)>();
        for (int count = 0; count < plans.Length * Types.Length; count++)
        {
            var request = await Wait(host.Cache.DeltaCreated.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Contains(request.Node.Id, plans.Select(plan => plan.Node));
            Assert.Contains(request.TypeUrl, Types);
            Assert.True(registrations.Add((request.Node.Id, request.TypeUrl)));
        }
    }

    private static DeltaDiscoveryRequest NodeRequest(string node, string type, string nonce = "")
    {
        var request = Request(type, nonce);
        request.Node.Id = node;
        return request;
    }

    private static Snapshot SnapshotForTypes(string version, params string[] names) =>
        new(version, Types.ToDictionary(type => type,
            type => (IEnumerable<IMessage?>)names.Select(name => Message(type, name)).ToArray(), StringComparer.Ordinal));

    private static int PendingWatches(GrpcHost host) =>
        host.Cache.Inner.GetStatusKeys().Sum(key => host.Cache.Inner.GetStatusInfo(key)!.NumDeltaWatches);

    private static List<T> Drain<T>(ChannelReader<T> reader)
    {
        var values = new List<T>();
        while (reader.TryRead(out var value)) values.Add(value);
        return values;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record NodePlan(string Node, bool Immediate, bool Cancel);
    private sealed record MemorySample(int Round, int PendingWatches, long ManagedBytes, long WorkingSetBytes);
}
