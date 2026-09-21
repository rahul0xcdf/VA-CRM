using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// One dated rate-and-threshold row for a <see cref="TaxSection"/>
/// (phase-7-tds-payroll-banking.md §2, checkpoints C7.3 and C7.4).
/// </summary>
/// <remarks>
/// <para>
/// Shaped after <c>GstRateSchedule</c> deliberately: rates change by notification, so a rate is a
/// dated row and never a constant, and computation resolves whichever schedule covers the payment's
/// business date. A change effective 1 July applies to payments on or after 1 July and to nothing
/// earlier (C7.4). The current row is simply the open-ended one.
/// </para>
/// <para>
/// <b>Two thresholds, because the statute has two.</b> Section 194C deducts when a single payment
/// exceeds ₹30,000 <em>or</em> when the year's payments to that contractor exceed ₹1,00,000 in
/// aggregate — whichever comes first. Modelling only the annual figure would miss a lone ₹50,000
/// payment; modelling only the single-payment figure would miss ten payments of ₹20,000. Either
/// threshold set to zero means "does not apply", so a section with only one of them needs no special
/// case (C7.3).
/// </para>
/// <para>
/// <b>The no-PAN rate is a rate, so it is data too.</b> Section 206AA charges a higher rate where the
/// payee has furnished no PAN. It sits here rather than as a constant because that higher rate is
/// itself set by statute and has changed.
/// </para>
/// </remarks>
public sealed class TaxRateSchedule : Entity, IAuditStamped
{
    private TaxRateSchedule()
    {
        // EF Core materialisation.
    }

    public TaxRateSchedule(
        Guid companyId,
        Guid taxSectionId,
        PayeeCategory payeeCategory,
        long ratePercentMinor,
        DateOnly effectiveFrom,
        long singleTransactionThresholdMinor = 0L,
        long annualThresholdMinor = 0L,
        long? rateWithoutPanPercentMinor = null,
        DateOnly? effectiveTo = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (taxSectionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A rate schedule must name its section master row.", nameof(taxSectionId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ratePercentMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(singleTransactionThresholdMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(annualThresholdMinor);

        if (rateWithoutPanPercentMinor is < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rateWithoutPanPercentMinor),
                rateWithoutPanPercentMinor,
                "A no-PAN rate cannot be negative.");
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveTo),
                effectiveTo,
                "A rate schedule cannot end before it begins.");
        }

        CompanyId = companyId;
        TaxSectionId = taxSectionId;
        PayeeCategory = payeeCategory;
        RatePercentMinor = ratePercentMinor;
        RateWithoutPanPercentMinor = rateWithoutPanPercentMinor;
        SingleTransactionThresholdMinor = singleTransactionThresholdMinor;
        AnnualThresholdMinor = annualThresholdMinor;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid CompanyId { get; private set; }

    public Guid TaxSectionId { get; private set; }

    /// <summary>The class of payee this row prices; <see cref="Tds.PayeeCategory.Any"/> matches all.</summary>
    public PayeeCategory PayeeCategory { get; private set; }

    /// <summary>
    /// The rate as a percentage in minor units of 1/10,000 of a percent (ADR-0003): 1% is 10_000,
    /// 0.1% is 1_000.
    /// </summary>
    public long RatePercentMinor { get; private set; }

    /// <summary>
    /// The higher rate where the payee furnished no PAN (Section 206AA), or null where the section
    /// has no such uplift.
    /// </summary>
    public long? RateWithoutPanPercentMinor { get; private set; }

    /// <summary>
    /// Deduct once a single payment exceeds this. Zero means the section has no single-payment test.
    /// </summary>
    public long SingleTransactionThresholdMinor { get; private set; }

    /// <summary>
    /// Deduct once the financial year's payments to this payee exceed this in aggregate. Zero means
    /// the section has no annual test.
    /// </summary>
    public long AnnualThresholdMinor { get; private set; }

    /// <summary>First business date the rate applies to.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last business date the rate applies to; null means still current.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Whether this schedule governs payments made on the given business date.</summary>
    public bool Covers(DateOnly date) =>
        date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    /// <summary>Whether this schedule prices the given class of payee.</summary>
    public bool Prices(PayeeCategory category) =>
        PayeeCategory == PayeeCategory.Any || PayeeCategory == category;

    /// <summary>
    /// Ends this schedule at its last effective day so a newer row takes over from the next date.
    /// History is closed, never rewritten.
    /// </summary>
    public void CloseAsOf(DateOnly lastEffectiveDate)
    {
        if (lastEffectiveDate < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastEffectiveDate),
                lastEffectiveDate,
                "A rate schedule cannot close before it begins.");
        }

        EffectiveTo = lastEffectiveDate;
    }
}
