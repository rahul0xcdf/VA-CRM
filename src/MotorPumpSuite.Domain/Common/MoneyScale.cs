using System.Globalization;

namespace MotorPumpSuite.Domain.Common;

/// <summary>
/// Converts between the domain's <see cref="decimal"/> money and the scaled 64-bit integer it is
/// persisted as.
/// </summary>
/// <remarks>
/// ADR-0003. SQLite has no decimal type: EF Core maps <see cref="decimal"/> to <c>TEXT</c>, and
/// <c>SUM()</c> over a TEXT-affinity column aggregates in IEEE-754 double. Storing minor units of
/// 1/10,000 makes every aggregation exact integer arithmetic, so the trial-balance invariant
/// <c>Σ = 0</c> holds exactly rather than within a tolerance.
/// <para>
/// Nothing here rounds. A value carrying more than four decimal places is a programming error, not
/// something to silently truncate — rounding is a business decision (GST round-off, invoice
/// rounding) that belongs in the domain where it can be audited.
/// </para>
/// </remarks>
public static class MoneyScale
{
    /// <summary>Decimal places retained — matches <c>decimal(18,4)</c>.</summary>
    public const int Places = 4;

    /// <summary>Minor units per major unit: 10^<see cref="Places"/>.</summary>
    public const long UnitsPerMajor = 10_000L;

    /// <summary>Largest representable amount, ≈ 9.22 × 10^14.</summary>
    public static decimal MaxValue => long.MaxValue / (decimal)UnitsPerMajor;

    /// <summary>Smallest representable amount.</summary>
    public static decimal MinValue => long.MinValue / (decimal)UnitsPerMajor;

    /// <summary>
    /// Converts a domain amount to minor units.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value has more than <see cref="Places"/> decimal places, or falls outside the
    /// representable range. Both are defects rather than data conditions, so neither is absorbed.
    /// </exception>
    public static long ToMinor(decimal value)
    {
        if (value < MinValue || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Amount is outside the representable range [{MinValue}, {MaxValue}].");
        }

        var scaled = value * UnitsPerMajor;
        var truncated = decimal.Truncate(scaled);

        if (scaled != truncated)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Amount has more than {Places} decimal places and would lose precision. " +
                "Round it explicitly in the domain before persisting — see ADR-0003.");
        }

        return (long)truncated;
    }

    /// <summary>Converts minor units back to a domain amount.</summary>
    public static decimal FromMinor(long minor) => minor / (decimal)UnitsPerMajor;

    /// <summary>
    /// Rounds a value to the storable scale using banker's rounding.
    /// </summary>
    /// <remarks>
    /// Deliberately explicit: callers that legitimately produce more than four places — a margin or
    /// tax computation — round on purpose by calling this, so the rounding appears in the code and
    /// in review rather than happening invisibly inside the persistence layer.
    /// </remarks>
    public static decimal Round(decimal value) =>
        Math.Round(value, Places, MidpointRounding.ToEven);

    /// <summary>Formats an amount for display or logging, invariant of the host's locale.</summary>
    public static string ToInvariantString(decimal value) =>
        value.ToString("F" + Places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
