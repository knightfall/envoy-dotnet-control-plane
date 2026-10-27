using Microsoft.Extensions.Time.Testing;

namespace Envoy.ControlPlane.Tests.Support;

internal sealed class TrackingTimeProvider : TimeProvider
{
    private readonly FakeTimeProvider clock = new();
    private int activeTimers;
    internal int ActiveTimers => Volatile.Read(ref activeTimers);
    internal void Advance(TimeSpan amount) => clock.Advance(amount);
    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
    public override long GetTimestamp() => clock.GetTimestamp();
    public override long TimestampFrequency => clock.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var inner = clock.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref activeTimers);
        return new TrackedTimer(inner, () => Interlocked.Decrement(ref activeTimers));
    }

    private sealed class TrackedTimer(ITimer inner, Action released) : ITimer
    {
        private int disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            inner.Dispose();
            released();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            await inner.DisposeAsync();
            released();
        }
    }
}
