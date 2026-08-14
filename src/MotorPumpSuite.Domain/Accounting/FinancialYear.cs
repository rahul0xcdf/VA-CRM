using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// One financial year for a company, e.g. 1 Apr 2026 – 31 Mar 2027.
/// </summary>
/// <remarks>
/// Dates are <see cref="DateOnly"/>, not <see cref="DateTimeOffset"/>: a financial year is a
/// business date range in the local calendar, and expressing 1 April in UTC would put some
/// vouchers in the wrong year depending on the hour they were entered.
/// </remarks>
public sealed class FinancialYear : Entity, IAuditStamped
{
    private FinancialYear()
    {
        // EF Core materialisation.
    }

    public FinancialYear(Guid companyId, DateOnly startDate, DateOnly endDate, string? label = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (endDate <= startDate)
        {
            throw new ArgumentException("A financial year must end after it starts.", nameof(endDate));
        }

        CompanyId = companyId;
        StartDate = startDate;
        EndDate = endDate;
        Label = string.IsNullOrWhiteSpace(label) ? BuildLabel(startDate, endDate) : label.Trim();
    }

    public Guid CompanyId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>Display label, e.g. <c>2026-27</c>.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>A closed year rejects new postings outright.</summary>
    public bool IsClosed { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    public void Close() => IsClosed = true;

    public void Reopen() => IsClosed = false;

    /// <summary>
    /// Derives the year containing <paramref name="date"/> for a company whose year starts in
    /// <paramref name="startMonth"/> (4 = April in India).
    /// </summary>
    public static (DateOnly Start, DateOnly End) RangeFor(DateOnly date, int startMonth)
    {
        if (startMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(startMonth), startMonth, "Month must be 1–12.");
        }

        // A date before the start month belongs to the year that opened in the previous calendar year.
        var startYear = date.Month >= startMonth ? date.Year : date.Year - 1;

        var start = new DateOnly(startYear, startMonth, 1);
        return (start, start.AddYears(1).AddDays(-1));
    }

    private static string BuildLabel(DateOnly start, DateOnly end) =>
        start.Year == end.Year
            ? start.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{start.Year}-{end.Year % 100:D2}";
}
