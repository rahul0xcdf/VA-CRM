namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// The current time, injected so that dated logic is testable.
/// </summary>
/// <remarks>
/// Almost everything downstream is date-sensitive: financial-year boundaries, GST rate effective
/// dates, TDS thresholds, e-way bill validity, the 24-hour IRN cancellation window. None of that
/// can be tested against <c>DateTime.Now</c>.
/// </remarks>
public interface IClock
{
    /// <summary>Current instant in UTC. Use for audit and system timestamps.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Today's date in the company's local calendar. Use for business dates — Indian financial-year
    /// logic follows the local date, not UTC.
    /// </summary>
    DateOnly Today { get; }
}
