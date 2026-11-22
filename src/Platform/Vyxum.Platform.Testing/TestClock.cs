namespace Vyxum.Platform.Testing;

/// <summary>
/// A <see cref="TimeProvider"/> that follows the real clock plus offsets tests can move: <see cref="Advance"/> sets the
/// wall clock (as a user changing the computer's clock does), <see cref="Elapse"/> lets time pass (the wall clock and
/// the monotonic timestamps of <see cref="GetTimestamp"/> both move). Timers stay real.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private long _offsetTicks;
    private long _monotonicOffsetTicks;

    /// <summary>Moves the wall clock by <paramref name="by"/> (also backwards); monotonic time does not move.</summary>
    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    /// <summary>Lets <paramref name="by"/> pass: moves the wall clock and the monotonic timestamps forward.</summary>
    public void Elapse(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Interlocked.Add(ref _offsetTicks, by.Ticks);
        Interlocked.Add(ref _monotonicOffsetTicks, by.Ticks);
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));

    /// <inheritdoc />
    public override long GetTimestamp() =>
        base.GetTimestamp() + (long)(Interlocked.Read(ref _monotonicOffsetTicks) * ((double)TimestampFrequency / TimeSpan.TicksPerSecond));
}
