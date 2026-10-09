using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// What one employee is paid under one pay head, from a given date
/// (phase-7-tds-payroll-banking.md §4a D3 — the employee's salary structure).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dated, like every other figure in this phase.</b> A raise in October must not change what
/// September's pay slip said, and re-running an old month — which happens, because arrears and
/// corrections happen — has to produce the month's own figures. So a structure line is appended and
/// the previous one closed, exactly as a rate schedule is, rather than edited in place.
/// </para>
/// <para>
/// <see cref="AmountMinor"/> and <see cref="PercentMinor"/> are both nullable because which one
/// means anything depends on the head's basis, and a flat-amount head carrying a stale percentage
/// is a figure waiting to be used by mistake. A statutory head carries neither: its figure is not
/// the company's to state.
/// </para>
/// </remarks>
public sealed class SalaryStructureLine : Entity, IAuditStamped
{
    private SalaryStructureLine()
    {
        // EF Core materialisation.
    }

    public SalaryStructureLine(
        Guid companyId,
        Guid employeeId,
        Guid payHeadId,
        PayComputationBasis basis,
        DateOnly effectiveFrom,
        long? amountMinor = null,
        long? percentMinor = null,
        DateOnly? effectiveTo = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException(
                "A structure line must name its employee.", nameof(employeeId));
        }

        if (payHeadId == Guid.Empty)
        {
            throw new ArgumentException(
                "A structure line must name its pay head.", nameof(payHeadId));
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveTo), effectiveTo, "A structure line cannot end before it begins.");
        }

        ValidateForBasis(basis, amountMinor, percentMinor);

        CompanyId = companyId;
        EmployeeId = employeeId;
        PayHeadId = payHeadId;
        Basis = basis;
        AmountMinor = amountMinor;
        PercentMinor = percentMinor;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid CompanyId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid PayHeadId { get; private set; }

    /// <summary>
    /// Copied from the head at the time the line was written.
    /// </summary>
    /// <remarks>
    /// A snapshot, not a join — the same reasoning a posted deduction snapshots its rate. Changing
    /// a head's basis later must not silently reinterpret every structure line that was entered
    /// under the old one: a "5,000" that meant rupees does not become 5,000% of basic.
    /// </remarks>
    public PayComputationBasis Basis { get; private set; }

    /// <summary>The rupee figure, in minor units, for a flat-amount line.</summary>
    public long? AmountMinor { get; private set; }

    /// <summary>The percentage in minor units of 1/10,000 of a percent: 40% is 400_000.</summary>
    public long? PercentMinor { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last date this line applies; null means it is the one currently in force.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Whether this line governs a payroll run for the given month-end date.</summary>
    public bool Covers(DateOnly date) =>
        date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    /// <summary>Ends this line so a revised one takes over from the next day.</summary>
    public void CloseAsOf(DateOnly lastEffectiveDate)
    {
        if (lastEffectiveDate < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastEffectiveDate),
                lastEffectiveDate,
                "A structure line cannot close before it begins.");
        }

        EffectiveTo = lastEffectiveDate;
    }

    private static void ValidateForBasis(
        PayComputationBasis basis,
        long? amountMinor,
        long? percentMinor)
    {
        switch (basis)
        {
            case PayComputationBasis.FlatAmount:
                if (amountMinor is not { } amount || amount < 0L)
                {
                    throw new ArgumentException(
                        "A flat-amount line needs a non-negative amount.", nameof(amountMinor));
                }

                break;

            case PayComputationBasis.PercentOfBasic:
            case PayComputationBasis.PercentOfGross:
                if (percentMinor is not { } percent || percent < 0L)
                {
                    throw new ArgumentException(
                        "A percentage line needs a non-negative percentage.", nameof(percentMinor));
                }

                break;

            case PayComputationBasis.Statutory:
                if (amountMinor.HasValue || percentMinor.HasValue)
                {
                    throw new ArgumentException(
                        "A statutory line carries no figure of its own — the rate, ceiling or slab "
                        + "rows produce it (§2, non-negotiable #6).",
                        nameof(basis));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown computation basis.");
        }
    }
}
