using System.Threading.Channels;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Task-based consumption of the channel-based SotW producer contract.</summary>
public static class ConfigWatcherExtensions
{
    /// <summary>Registers a one-shot response with an individual buffer and awaitable completion.</summary>
    /// <param name="watcher">The configuration producer.</param>
    /// <param name="request">The stable originating request.</param>
    /// <param name="subscription">Subscription metadata stable for the registration lifetime.</param>
    /// <exception cref="InvalidOperationException">The producer returned no registration.</exception>
    public static IWatch<ISotwResponse> CreateWatch(this IConfigWatcher watcher, DiscoveryRequest request, ISubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        var channel = Channel.CreateBounded<ISotwResponse>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var registration = watcher.CreateWatch(request, subscription, channel.Writer)
            ?? throw new InvalidOperationException("Missing watch registration");
        return new TaskWatch<ISotwResponse>(registration, channel.Reader);
    }

    private sealed class TaskWatch<TResponse> : IWatch<TResponse>, IAsyncDisposable where TResponse : IWatchResponse
    {
        private readonly IDisposable registration;
        private readonly CancellationTokenSource cancellation = new();
        private int disposed;

        internal TaskWatch(IDisposable registration, ChannelReader<TResponse> responses)
        {
            this.registration = registration;
            Completion = responses.ReadAsync(cancellation.Token).AsTask();
        }

        public Task<TResponse> Completion { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            try
            {
                registration.Dispose();
            }
            finally
            {
                cancellation.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            bool first = Interlocked.Exchange(ref disposed, 1) == 0;
            try
            {
                if (registration is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync();
                }
                else if (first)
                {
                    registration.Dispose();
                }
            }
            finally
            {
                if (first)
                {
                    cancellation.Cancel();
                }
                cancellation.Dispose();
            }
        }
    }
}
