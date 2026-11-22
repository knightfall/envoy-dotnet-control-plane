namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal static class AsyncPoll
{
    internal static Task UntilAsync(Func<bool> condition, string description, TimeSpan timeout, CancellationToken cancellationToken) =>
        UntilAsync(_ => Task.FromResult(condition()), description, timeout, cancellationToken);

    internal static async Task UntilAsync(Func<CancellationToken, Task<bool>> condition, string description,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            while (!await condition(deadline.Token).WaitAsync(deadline.Token))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }
}
