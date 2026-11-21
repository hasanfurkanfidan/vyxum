namespace Vyxum.Platform.Testing;

/// <summary>A <see cref="TimeProvider"/> that follows the real clock plus an offset tests can move forward.</summary>
public sealed class TestClock : TimeProvider
{
    private long _offsetTicks;

    /// <summary>Moves the clock forward by <paramref name="by"/>.</summary>
    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));
}
