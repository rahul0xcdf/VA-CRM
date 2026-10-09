using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// One month's payroll run: every employee's pay slip, and the journal voucher it posted through
/// (phase-7-tds-payroll-banking.md §4a D3 — "a salary voucher posting through the journal engine,
/// exactly as the Phase 2 stock voucher does").
/// </summary>
/// <remarks>
/// <para>
/// <b>It mirrors <c>StockVoucher</c> deliberately.</b> Like a stock voucher, it is a document with
/// its own lines that <em>also</em> produces accounting, and like a stock voucher it does not write
/// journal lines itself — it carries <see cref="PostedVoucherId"/>, the id of the voucher the
/// Phase 1 engine posted. One movement, one number, one place the books are written.
/// </para>
/// <para>
/// <b>The period is a month, identified by its last day.</b> Payroll is monthly, every statutory
/// figure is monthly, and every challan is monthly. Storing the period end rather than a year and a
/// month pair means the dated rate, slab and structure rows are all resolved by the same
/// <c>Covers(date)</c> call every other dated row in this codebase uses.
/// </para>
/// </remarks>
public sealed class SalaryVoucher : Entity, IAuditStamped
{
    private readonly List<SalaryVoucherLine> _lines = [];

    private SalaryVoucher()
    {
        // EF Core materialisation.
    }

    public SalaryVoucher(Guid companyId, DateOnly periodEnd, string? narration = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        PeriodEnd = periodEnd;
        PeriodStart = new DateOnly(periodEnd.Year, periodEnd.Month, 1);
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>First day of the month this run covers.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>
    /// Last day of the month, and the date every dated row is resolved against — the rate in force
    /// at month end is the rate the month is computed at.
    /// </summary>
    public DateOnly PeriodEnd { get; private set; }

    public string? Narration { get; private set; }

    /// <summary>The journal voucher this run posted through; empty until it posts.</summary>
    public Guid PostedVoucherId { get; private set; }

    /// <summary>The posted voucher's number, so the run displays without a join.</summary>
    public string PostedVoucherNumber { get; private set; } = string.Empty;

    public IReadOnlyList<SalaryVoucherLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Total of every earning line across every employee.</summary>
    public long GrossEarningsMinor => Sum(PayHeadType.Earning);

    /// <summary>Total withheld from employees.</summary>
    public long TotalDeductionsMinor => Sum(PayHeadType.Deduction);

    /// <summary>Total the company pays on top of gross.</summary>
    public long EmployerContributionsMinor => Sum(PayHeadType.EmployerContribution);

    /// <summary>What the employees are actually owed.</summary>
    public long NetPayableMinor => GrossEarningsMinor - TotalDeductionsMinor;

    /// <summary>How many people this run paid.</summary>
    public int EmployeeCount => _lines.Select(line => line.EmployeeId).Distinct().Count();

    /// <summary>Adds one computed pay-slip line.</summary>
    public SalaryVoucherLine AddLine(
        Guid employeeId,
        Guid payHeadId,
        Guid ledgerId,
        string payHeadName,
        PayHeadType type,
        long amountMinor,
        StatuteKind? statute = null,
        int sortOrder = 0)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A pay slip line must name its employee.", nameof(employeeId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(amountMinor);
        ArgumentException.ThrowIfNullOrWhiteSpace(payHeadName);

        var line = new SalaryVoucherLine(
            CompanyId,
            Id,
            employeeId,
            payHeadId,
            ledgerId,
            payHeadName.Trim(),
            type,
            amountMinor,
            statute,
            sortOrder);

        _lines.Add(line);

        return line;
    }

    /// <summary>Every line belonging to one employee, in pay-slip order.</summary>
    public IReadOnlyList<SalaryVoucherLine> LinesFor(Guid employeeId) =>
        _lines.Where(line => line.EmployeeId == employeeId)
            .OrderBy(line => line.Type)
            .ThenBy(line => line.SortOrder)
            .ToList();

    /// <summary>Records the journal voucher this run posted through.</summary>
    public void AttachPostedVoucher(Guid voucherId, string number)
    {
        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException("A posted voucher id is required.", nameof(voucherId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        PostedVoucherId = voucherId;
        PostedVoucherNumber = number.Trim();
    }

    private long Sum(PayHeadType type) =>
        _lines.Where(line => line.Type == type).Sum(line => line.AmountMinor);
}
