using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// What was deducted, on what, under which section and at which rate — captured when the voucher
/// posts (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A snapshot, not a reference.</b> The rate, the threshold that triggered it and the certificate
/// number are copied onto this row rather than looked up through the section master, for the same
/// reason Phase 5 snapshots GST onto a trade document: a return filed in July states what was
/// deducted in June, and a rate row edited in August must not retroactively change what the filed
/// return said. Resolving through the master at read time would do exactly that.
/// </para>
/// <para>
/// <b>It is also the cumulative ledger.</b> Checkpoint C7.3's annual threshold is "payments to this
/// payee under this section this financial year", and these rows are that history — which is why
/// <see cref="CumulativeBaseAfterMinor"/> is stored rather than recomputed. A running total derived
/// by summing on every posting would give a different answer once a voucher is cancelled, and the
/// threshold an earlier payment was judged against must not move.
/// </para>
/// </remarks>
public sealed class TaxDeduction : Entity, ICreationStamped, IBelongsToAggregate
{
    private TaxDeduction()
    {
        // EF Core materialisation.
    }

    public TaxDeduction(
        Guid companyId,
        Guid voucherId,
        Guid ledgerId,
        Guid taxSectionId,
        Guid financialYearId,
        TaxSectionKind kind,
        string sectionCode,
        PayeeCategory payeeCategory,
        DateOnly deductionDate,
        long baseAmountMinor,
        TaxDeductionOutcome outcome,
        bool payeeHadPan,
        string? certificateNumber = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException(
                "A deduction must name the voucher that made it.", nameof(voucherId));
        }

        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A deduction must name the party it was made from.", nameof(ledgerId));
        }

        if (taxSectionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A deduction must name the section it was made under.", nameof(taxSectionId));
        }

        if (financialYearId == Guid.Empty)
        {
            throw new ArgumentException(
                "A deduction must name the financial year it counts against.", nameof(financialYearId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sectionCode);
        ArgumentOutOfRangeException.ThrowIfNegative(baseAmountMinor);

        CompanyId = companyId;
        VoucherId = voucherId;
        LedgerId = ledgerId;
        TaxSectionId = taxSectionId;
        FinancialYearId = financialYearId;
        Kind = kind;
        SectionCode = sectionCode.Trim().ToUpperInvariant();
        PayeeCategory = payeeCategory;
        DeductionDate = deductionDate;
        BaseAmountMinor = baseAmountMinor;
        TaxableBaseMinor = outcome.TaxableBaseMinor;
        DeductedAmountMinor = outcome.DeductedAmountMinor;
        AppliedRatePercentMinor = outcome.EffectiveRatePercentMinor;
        CumulativeBaseAfterMinor = outcome.CumulativeBaseAfterMinor;
        IsCertificateApplied = outcome.IsCertificateApplied;
        IsThresholdCrossingPayment = outcome.IsThresholdCrossingPayment;
        PayeeHadPan = payeeHadPan;
        CertificateNumber = string.IsNullOrWhiteSpace(certificateNumber) ? null : certificateNumber.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The voucher this deduction posted with, and the aggregate it is audited under.</summary>
    public Guid VoucherId { get; private set; }

    /// <summary>The party deducted from.</summary>
    public Guid LedgerId { get; private set; }

    public Guid TaxSectionId { get; private set; }

    /// <summary>The financial year the cumulative threshold counts within.</summary>
    public Guid FinancialYearId { get; private set; }

    public TaxSectionKind Kind { get; private set; }

    /// <summary>The section code as it stood at deduction time, for the return to quote.</summary>
    public string SectionCode { get; private set; } = string.Empty;

    public PayeeCategory PayeeCategory { get; private set; }

    public DateOnly DeductionDate { get; private set; }

    /// <summary>The payment itself.</summary>
    public long BaseAmountMinor { get; private set; }

    /// <summary>
    /// What was actually taxed. Larger than <see cref="BaseAmountMinor"/> on the payment that crosses
    /// the annual threshold and catches up on earlier untaxed ones.
    /// </summary>
    public long TaxableBaseMinor { get; private set; }

    public long DeductedAmountMinor { get; private set; }

    /// <summary>The rate as applied, blended where a capped certificate covered only part.</summary>
    public long AppliedRatePercentMinor { get; private set; }

    /// <summary>The year's running total including this payment, frozen as judged.</summary>
    public long CumulativeBaseAfterMinor { get; private set; }

    public bool IsCertificateApplied { get; private set; }

    /// <summary>True where this payment is the one that crossed the annual aggregate (C7.3).</summary>
    public bool IsThresholdCrossingPayment { get; private set; }

    /// <summary>Whether a PAN was on file, which is why the rate was what it was (Section 206AA).</summary>
    public bool PayeeHadPan { get; private set; }

    /// <summary>The Section 197 certificate number, where one priced this deduction.</summary>
    public string? CertificateNumber { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>The voucher, so an edit-log row for this deduction groups under it (§11.1).</summary>
    string IBelongsToAggregate.AggregateType => nameof(Accounting.Voucher);

    /// <inheritdoc />
    string IBelongsToAggregate.AggregateId => VoucherId.ToString("D");
}
