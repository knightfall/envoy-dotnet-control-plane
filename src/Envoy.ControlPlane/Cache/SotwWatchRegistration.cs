using System.Threading.Channels;

namespace Envoy.ControlPlane.Cache;

internal sealed class SotwWatchRegistration(object gate, ChannelWriter<ISotwResponse> responses, Action? release)
    : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private Task<bool>? delivery;
    private Task? disposal;
    private bool disposed;

    internal Task<bool> DeliverAsync(ISotwResponse response, CancellationToken publication)
    {
        lock (gate)
        {
            if (disposed)
            {
                return Task.FromResult(false);
            }
            if (delivery is { IsCompleted: false })
            {
                return delivery;
            }
            delivery = WriteAsync(response, publication);
            return delivery;
        }
    }

    private async Task<bool> WriteAsync(ISotwResponse response, CancellationToken publication)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, publication);
        try
        {
            await responses.WriteAsync(response, linked.Token);
            lock (gate)
            {
                release?.Invoke();
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            release?.Invoke();
            cancellation.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new ValueTask(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        Dispose();
        if (delivery is not null)
        {
            try
            {
                await delivery;
            }
            catch (OperationCanceledException)
            {
                // Publication owns its cancellation result; disposal still releases the registration.
            }
            catch (Exception) when (delivery.IsFaulted)
            {
                // Publication or the destination reports delivery failures, rather than cleanup duplicating them.
                _ = delivery.Exception;
            }
        }
        cancellation.Dispose();
    }
}
