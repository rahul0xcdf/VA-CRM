using MotorPumpSuite.Domain.Abstractions;

namespace MotorPumpSuite.Application.Common;

/// <summary>
/// The real clock. Tests substitute a fixed <see cref="IClock"/> instead.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
