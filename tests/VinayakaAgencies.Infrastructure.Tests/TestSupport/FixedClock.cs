using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// A clock the test controls, so audit stamps can be asserted exactly.
/// </summary>
public sealed class FixedClock : IClock
{
    public FixedClock()
        : this(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero))
    {
    }

    public FixedClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);

    /// <summary>Moves the clock forward, so a modification stamp differs from its creation stamp.</summary>
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
