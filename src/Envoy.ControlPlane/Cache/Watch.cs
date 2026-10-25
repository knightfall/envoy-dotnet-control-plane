namespace Envoy.ControlPlane.Cache;

/// <summary>Completes one response and releases its producer registration exactly once.</summary>
/// <typeparam name="T">The cache response type.</typeparam>
public sealed class Watch<T> : IWatch<T>
{
    private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action? releaseRegistration;
    private int completionClaimed;

    /// <summary>Creates a pending watch with optional registration cleanup.</summary>
    /// <param name="releaseRegistration">Producer cleanup; it must not perform stream or network work.</param>
    public Watch(Action? releaseRegistration = null)
    {
        this.releaseRegistration = releaseRegistration;
    }

    /// <inheritdoc/>
    public Task<T> Completion => completion.Task;

    /// <summary>Delivers the response if the watch is still pending.</summary>
    /// <param name="response">The stable cache response.</param>
    public bool TrySetResult(T response)
    {
        if (!TryClaimCompletion())
        {
            return false;
        }
        try
        {
            releaseRegistration?.Invoke();
        }
        finally
        {
            completion.SetResult(response);
        }
        return true;
    }

    /// <summary>Faults the watch if it is still pending.</summary>
    /// <param name="exception">The producer failure to preserve.</param>
    public bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!TryClaimCompletion())
        {
            return false;
        }
        try
        {
            releaseRegistration?.Invoke();
        }
        finally
        {
            completion.SetException(exception);
        }
        return true;
    }

    /// <summary>Cancels a pending watch; repeated disposal leaves its first outcome unchanged.</summary>
    public void Dispose()
    {
        if (!TryClaimCompletion())
        {
            return;
        }
        try
        {
            releaseRegistration?.Invoke();
        }
        finally
        {
            completion.SetCanceled();
        }
    }

    /// <summary>Claims before cleanup so disposal cannot replace a response being delivered.</summary>
    private bool TryClaimCompletion() => Interlocked.CompareExchange(ref completionClaimed, 1, 0) == 0;
}
