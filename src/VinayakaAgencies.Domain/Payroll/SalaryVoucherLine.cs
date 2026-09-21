using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// One line of one employee's pay slip, as computed and frozen when the salary voucher posted
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// <b>A snapshot, exactly like a posted tax deduction.</b> The head's name, its type and the figure
/// are copied here rather than resolved through the master at read time. A pay slip issued in May
/// has to say in December what it said in May, and a pay head renamed or a structure line revised
/// in between must not rewrite it. Resolving through the master would do precisely that.
/// </para>
/// <para>
/// This is also the row the PF, ESI and PT challan reports are built from, which is why a statutory
/// line carries <see cref="Statute"/> — the report groups on it rather than matching head names.
/// </para>
/// </remarks>
public sealed class SalaryVoucherLine : Entity, ICreationStamped, IBelongsToAggregate
{
    private SalaryVoucherLine()
    {
        // EF Core materialisation.
    }

    internal SalaryVoucherLine(
        Guid companyId,
        Guid salaryVoucherId,
        Guid employeeId,
        Guid payHeadId,
        Guid ledgerId,
        string payHeadName,
        PayHeadType type,
        long amountMinor,
        StatuteKind? statute,
        int sortOrder)
    {
        CompanyId = companyId;
        SalaryVoucherId = salaryVoucherId;
        EmployeeId = employeeId;
        PayHeadId = payHeadId;
        LedgerId = ledgerId;
        PayHeadName = payHeadName;
        Type = type;
        AmountMinor = amountMinor;
        Statute = statute;
        SortOrder = sortOrder;
    }

    public Guid CompanyId { get; private set; }

    public Guid SalaryVoucherId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid PayHeadId { get; private set; }

    /// <summary>The ledger this line posted to, snapshotted from the head.</summary>
    public Guid LedgerId { get; private set; }

    /// <summary>The head's name as it stood when the slip was produced.</summary>
    public string PayHeadName { get; private set; } = string.Empty;

    public PayHeadType Type { get; private set; }

    public long AmountMinor { get; private set; }

    /// <summary>The statute that produced this figure, on a statutory line; null otherwise.</summary>
    public StatuteKind? Statute { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>The salary voucher, so an edit-log row files under the run it belongs to (§11.1).</summary>
    public string AggregateType => nameof(SalaryVoucher);

    /// <inheritdoc />
    public string AggregateId => SalaryVoucherId.ToString("D");
}
