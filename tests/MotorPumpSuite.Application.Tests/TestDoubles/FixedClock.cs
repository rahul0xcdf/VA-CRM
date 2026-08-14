using MotorPumpSuite.Domain.Abstractions;

namespace MotorPumpSuite.Application.Tests.TestDoubles;

/// <summary>
/// A clock that never moves, so date-sensitive logic (financial-year boundaries, GST effective
/// dates, TDS thresholds) can be tested deterministically instead of against the wall clock.
/// </summary>
public sealed class FixedClock(DateTimeOffset utcNow, DateOnly today) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;

    public DateOnly Today { get; } = today;
}
