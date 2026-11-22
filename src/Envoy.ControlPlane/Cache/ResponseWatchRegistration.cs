using System.Threading.Channels;

namespace Envoy.ControlPlane.Cache;

internal sealed class ResponseWatchRegistration<TResponse>(object gate, ChannelWriter<TResponse> responses, Action? release)
    : IDisposable, IAsyncDisposable where TResponse : IWatchResponse
{
    private readonly CancellationTokenSource cancellation = new();
    private Task<bool>? delivery;
    private TaskCompletionSource? disposal;

    internal Task<bool> DeliverAsync(TResponse response, CancellationToken publication)
    {
        TaskCompletionSource<bool> completion;
        lock (gate)
        {
            if (disposal is not null)
            {
                return Task.FromResult(false);
            }
            if (delivery is { IsCompleted: false })
            {
                return delivery;
            }
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            delivery = completion.Task;
        }
        // Register the in-flight write before calling the writer, which can synchronously reenter the cache.
        _ = WriteAsync(response, publication, completion);
        return completion.Task;
    }

    private async Task WriteAsync(TResponse response, CancellationToken publication, TaskCompletionSource<bool> completion)
    {
        try
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, publication))
            {
                await responses.WriteAsync(response, linked.Token);
            }
            lock (gate)
            {
                release?.Invoke();
            }
            completion.SetResult(true);
        }
        catch (OperationCanceledException error)
        {
            if (cancellation.IsCancellationRequested)
            {
                completion.SetResult(false);
            }
            else
            {
                completion.SetCanceled(error.CancellationToken);
            }
        }
        catch (Exception error)
        {
            completion.SetException(error);
            // Immediate deliveries have no awaiting publisher. Publication still receives its original failure.
            _ = completion.Task.Exception;
        }
    }

    public void Dispose()
    {
        TaskCompletionSource completion;
        Task? pending;
        lock (gate)
        {
            if (disposal is not null)
            {
                return;
            }
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = completion;
            pending = delivery;
            release?.Invoke();
        }
        _ = DisposeCoreAsync(pending, completion);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        lock (gate)
        {
            return new ValueTask(disposal!.Task);
        }
    }

    private async Task DisposeCoreAsync(Task? pending, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await cancellation.CancelAsync();
        }
        catch (Exception error)
        {
            failure = error;
        }
        if (pending is not null)
        {
            // The publisher owns the write result. Continue cleanup after a canceled or faulted delivery.
            await pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        cancellation.Dispose();
        if (failure is not null)
        {
            completion.SetException(failure);
            _ = completion.Task.Exception;
        }
        else
        {
            completion.SetResult();
        }
    }
}
