using System.Threading.Channels;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Xunit;

namespace Envoy.ControlPlane.Tests.Support;

internal static class HeartbeatFixtures
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    internal static ResourceEntry Entry(string name, TimeSpan? ttl = null) => new(new RouteConfiguration { Name = name }, ttl);

    internal static Snapshot Routes(string version, params ResourceEntry[] entries) => new(version,
        new Dictionary<string, IEnumerable<ResourceEntry>> { [ResourceTypeUrls.Route] = entries });

    internal static TestSubscription Known(params string[] names) => SnapshotCacheFixtures.Subscription(names,
        returned: names.ToDictionary(name => name, _ => "v1", StringComparer.Ordinal));

    internal static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    internal static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}

internal sealed class HeartbeatWriter(ChannelWriter<ISotwResponse> inner) : ChannelWriter<ISotwResponse>
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool TryComplete(Exception? error = null) => inner.TryComplete(error);
    public override bool TryWrite(ISotwResponse item) => inner.TryWrite(item);
    public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => inner.WaitToWriteAsync(cancellationToken);
    public override ValueTask WriteAsync(ISotwResponse item, CancellationToken cancellationToken = default)
    {
        Started.TrySetResult();
        return inner.WriteAsync(item, cancellationToken);
    }
}
