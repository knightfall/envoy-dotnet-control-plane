using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Tests.Support;

internal sealed class MuxProbeCache : IConfigWatcher, IConfigFetcher, IDisposable
{
    internal object? LastRequest { get; private set; }
    internal ISubscription? LastSubscription { get; private set; }
    internal object? LastWriter { get; private set; }
    internal ResponseContext LastContext { get; private set; }
    internal int Calls { get; private set; }
    internal int Disposals { get; private set; }
    internal Exception? Failure { get; init; }
    internal MuxProbeRegistration Registration { get; } = new();
    internal TaskCompletionSource<ISotwResponse> FetchCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IDisposable CreateWatch(DiscoveryRequest request, ISubscription subscription, ChannelWriter<ISotwResponse> responses)
    {
        Observe(request);
        LastSubscription = subscription;
        LastWriter = responses;
        return Registration;
    }

    public IDisposable CreateDeltaWatch(DeltaDiscoveryRequest request, ISubscription subscription, ChannelWriter<IDeltaResponse> responses)
    {
        Observe(request);
        LastSubscription = subscription;
        LastWriter = responses;
        return Registration;
    }

    public Task<ISotwResponse> FetchAsync(DiscoveryRequest request, ResponseContext context = default)
    {
        Observe(request);
        LastContext = context;
        return FetchCompletion.Task;
    }

    private void Observe(object request)
    {
        Calls++;
        LastRequest = request;
        if (Failure is { } failure)
        {
            throw failure;
        }
    }

    public void Dispose() => Disposals++;
}

internal sealed class MuxProbeRegistration : IDisposable, IAsyncDisposable
{
    internal bool Disposed { get; private set; }
    internal bool AsynchronouslyDisposed { get; private set; }
    public void Dispose() => Disposed = true;
    public ValueTask DisposeAsync()
    {
        AsynchronouslyDisposed = true;
        Dispose();
        return ValueTask.CompletedTask;
    }
}
