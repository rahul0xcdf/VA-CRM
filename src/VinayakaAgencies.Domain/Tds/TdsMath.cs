using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>Why a computation deducted nothing, so the screen can say so rather than show a blank.</summary>
public enum NoDeductionReason
{
    /// <summary>Tax was deducted; this is not a refusal.</summary>
    None = 0,

    /// <summary>The party is marked outside deduction on its payee profile.</summary>
    PayeeNotApplicable = 1,

    /// <summary>No rate schedule covers this section, payee class and date.</summary>
    NoScheduleInForce = 2,

    /// <summary>Neither the single-payment nor the annual threshold has been crossed yet.</summary>
    BelowThreshold = 3,

    /// <summary>A nil-rate Section 197 certificate covers this payment.</summary>
    NilCertificate = 4,

    /// <summary>The buyer is deducting 194Q on this transaction, so TCS must not also apply.</summary>
    BuyerDeductsInstead = 5,
}

/// <summary>What the computation decided, and everything needed to justify it afterwards.</summary>
/// <param name="TaxableBaseMinor">The amount actually taxed, which is not always the payment.</param>
/// <param name="DeductedAmountMinor">The tax, rounded to whole rupees.</param>
/// <param name="EffectiveRatePercentMinor">
/// The rate as applied. Derived rather than copied, because a part-certificate payment is taxed at
/// two rates and neither one describes it.
/// </param>
/// <param name="CumulativeBaseAfterMinor">The year's running total including this payment.</param>
/// <param name="IsCertificateApplied">True when a Section 197 certificate priced any part of it.</param>
/// <param name="IsThresholdCrossingPayment">
/// True when this is the payment that crossed the annual aggregate and therefore caught up on
/// earlier untaxed payments — the case checkpoint C7.3 pins.
/// </param>
/// <param name="Reason">Why nothing was deducted, where nothing was.</param>
public readonly record struct TaxDeductionOutcome(
    long TaxableBaseMinor,
    long DeductedAmountMinor,
    long EffectiveRatePercentMinor,
    long CumulativeBaseAfterMinor,
    bool IsCertificateApplied,
    bool IsThresholdCrossingPayment,
    NoDeductionReason Reason)
{
    /// <summary>True when tax is actually due on this payment.</summary>
    public bool HasDeduction => DeductedAmountMinor > 0L;
}

/// <summary>
/// The TDS/TCS arithmetic (phase-7-tds-payroll-banking.md §2, checkpoints C7.1, C7.3, C7.4).
/// </summary>
/// <remarks>
/// <para>
/// A pure static class holding no section number, no rate and no threshold — every figure arrives as
/// an argument read from a data row. That is what makes checkpoint C7.5 satisfiable and what makes
/// the April 2026 renumbering a data update: this file does not know 194C exists.
/// </para>
/// <para>
/// Separated from the service that loads the rows for the same reason <c>GstTaxMath</c> is: the rule
/// is arithmetic, and arithmetic is worth testing against hand-computed fixtures without a database
/// in the way.
/// </para>
/// </remarks>
public static class TdsMath
{
    /// <summary>Minor units in one rupee — the unit TDS is deducted and challaned in.</summary>
    private const long MinorUnitsPerRupee = MoneyScale.UnitsPerMajor;

    /// <summary>A percentage in minor units: 100% expressed on the 1/10,000-of-a-percent scale.</summary>
    private const long OneHundredPercentMinor = 100L * 10_000L;

    /// <summary>
    /// Computes the tax due on one payment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two threshold tests are not symmetric, because the statute's are not.</b> The
    /// single-payment test taxes <em>that payment</em>: one ₹50,000 contractor bill is deducted on
    /// even though the year's total is small, and earlier smaller payments are not reopened. The
    /// annual aggregate test taxes <em>the year</em>: the payment that carries the running total past
    /// the limit catches up on every rupee not yet taxed, including payments that were correctly let
    /// through before. Implementing only the second is the common bug, and it under-deducts on a lone
    /// large payment; implementing only the first under-deducts on a stream of small ones.
    /// </para>
    /// <para>
    /// The annual test is evaluated first and wins, because its base subsumes the single-payment
    /// one — once the year is taxable, this payment is inside the catch-up already.
    /// </para>
    /// </remarks>
    /// <param name="baseAmountMinor">This payment, before tax.</param>
    /// <param name="cumulativeBaseBeforeMinor">The year's payments to this payee under this section, before this one.</param>
    /// <param name="cumulativeTaxedBaseBeforeMinor">How much of that has already been taxed.</param>
    /// <param name="ratePercentMinor">The scheduled rate, or the no-PAN rate where one applies.</param>
    /// <param name="singleTransactionThresholdMinor">Single-payment threshold; zero means no such test.</param>
    /// <param name="annualThresholdMinor">Annual aggregate threshold; zero means no such test.</param>
    /// <param name="certificateRatePercentMinor">A Section 197 rate where one covers this payment.</param>
    /// <param name="certificateRoomMinor">How much base the certificate still authorises, or null when uncapped.</param>
    /// <returns>The decision, with everything needed to explain it.</returns>
    public static TaxDeductionOutcome Compute(
        long baseAmountMinor,
        long cumulativeBaseBeforeMinor,
        long cumulativeTaxedBaseBeforeMinor,
        long ratePercentMinor,
        long singleTransactionThresholdMinor,
        long annualThresholdMinor,
        long? certificateRatePercentMinor = null,
        long? certificateRoomMinor = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseAmountMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeBaseBeforeMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeTaxedBaseBeforeMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(ratePercentMinor);

        var cumulativeAfter = cumulativeBaseBeforeMinor + baseAmountMinor;

        var crossesAnnual = annualThresholdMinor > 0L && cumulativeAfter > annualThresholdMinor;
        var crossesSingle = singleTransactionThresholdMinor > 0L
            && baseAmountMinor > singleTransactionThresholdMinor;

        // A section configured with neither threshold deducts from the first rupee, which is how
        // 194Q and 206C(1H) behave once their own applicability flag is on.
        var hasNoThreshold = annualThresholdMinor == 0L && singleTransactionThresholdMinor == 0L;

        long taxableBase;
        bool isCrossingPayment;

        if (crossesAnnual)
        {
            // Catch up on everything this year that has not been taxed yet, this payment included.
            taxableBase = cumulativeAfter - cumulativeTaxedBaseBeforeMinor;
            isCrossingPayment = cumulativeBaseBeforeMinor <= annualThresholdMinor;
        }
        else if (crossesSingle || hasNoThreshold)
        {
            taxableBase = baseAmountMinor;
            isCrossingPayment = false;
        }
        else
        {
            return new TaxDeductionOutcome(
                TaxableBaseMinor: 0L,
                DeductedAmountMinor: 0L,
                EffectiveRatePercentMinor: 0L,
                CumulativeBaseAfterMinor: cumulativeAfter,
                IsCertificateApplied: false,
                IsThresholdCrossingPayment: false,
                Reason: NoDeductionReason.BelowThreshold);
        }

        if (taxableBase <= 0L)
        {
            return new TaxDeductionOutcome(
                0L, 0L, 0L, cumulativeAfter, false, false, NoDeductionReason.BelowThreshold);
        }

        var (tax, certificateApplied) = ApplyRates(
            taxableBase,
            ratePercentMinor,
            certificateRatePercentMinor,
            certificateRoomMinor);

        var reason = tax == 0L && certificateApplied
            ? NoDeductionReason.NilCertificate
            : NoDeductionReason.None;

        return new TaxDeductionOutcome(
            TaxableBaseMinor: taxableBase,
            DeductedAmountMinor: tax,
            EffectiveRatePercentMinor: DeriveEffectiveRate(taxableBase, tax),
            CumulativeBaseAfterMinor: cumulativeAfter,
            IsCertificateApplied: certificateApplied,
            IsThresholdCrossingPayment: isCrossingPayment,
            Reason: reason);
    }

    /// <summary>
    /// Prices the taxable base, splitting it where a capped certificate runs out partway.
    /// </summary>
    /// <remarks>
    /// A certificate authorising a lower rate "up to ₹5,00,000" does not stop applying at ₹5,00,001 —
    /// it applies to the first ₹5,00,000 and the balance is deducted normally. Treating the whole
    /// payment as certificate-rated over-claims the relief; treating it as fully normal-rated
    /// over-deducts. Both are wrong on a filed return, so the base is split.
    /// </remarks>
    private static (long Tax, bool CertificateApplied) ApplyRates(
        long taxableBaseMinor,
        long ratePercentMinor,
        long? certificateRatePercentMinor,
        long? certificateRoomMinor)
    {
        if (certificateRatePercentMinor is not { } certificateRate)
        {
            return (RoundToRupee(RawTax(taxableBaseMinor, ratePercentMinor)), false);
        }

        // Uncapped, or capped with room to spare: the whole base is certificate-rated.
        if (certificateRoomMinor is not { } room || room >= taxableBaseMinor)
        {
            return (RoundToRupee(RawTax(taxableBaseMinor, certificateRate)), true);
        }

        var atCertificateRate = Math.Max(0L, room);
        var atNormalRate = taxableBaseMinor - atCertificateRate;

        // Rounded once on the total rather than per part, so a split payment and an unsplit one of
        // the same size cannot differ by a rupee of rounding.
        var raw = RawTax(atCertificateRate, certificateRate) + RawTax(atNormalRate, ratePercentMinor);

        return (RoundToRupee(raw), atCertificateRate > 0L);
    }

    /// <summary>
    /// Tax before rounding, in minor units.
    /// </summary>
    /// <remarks>
    /// Both operands are already scaled by 10,000, so the product is scaled by 10^8 and the divisor
    /// restores it. Done in <see cref="decimal"/> rather than <see cref="long"/> because a large
    /// payment times a rate overflows 64 bits before the divide brings it back.
    /// </remarks>
    private static long RawTax(long baseMinor, long ratePercentMinor)
    {
        if (baseMinor <= 0L || ratePercentMinor <= 0L)
        {
            return 0L;
        }

        var scaled = (decimal)baseMinor * ratePercentMinor / OneHundredPercentMinor;

        return (long)decimal.Round(scaled, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Rounds tax to whole rupees.
    /// </summary>
    /// <remarks>
    /// TDS is deposited and challaned in whole rupees, so the figure posted to the books must be the
    /// figure that will be paid — carrying paise into the ledger would leave the challan and the
    /// liability account permanently a few paise apart. Away-from-zero at the midpoint, which is the
    /// convention returns are filed on. This is a business decision taken in the domain where it can
    /// be audited, exactly as <c>MoneyScale</c>'s remarks require.
    /// </remarks>
    public static long RoundToRupee(long amountMinor)
    {
        var rupees = decimal.Round((decimal)amountMinor / MinorUnitsPerRupee, 0, MidpointRounding.AwayFromZero);

        return (long)rupees * MinorUnitsPerRupee;
    }

    /// <summary>
    /// The rate the deduction actually worked out at, for the snapshot.
    /// </summary>
    /// <remarks>
    /// Derived from the two amounts rather than carried through from the schedule, so that a
    /// part-certificate payment reports the blended rate it was really taxed at instead of one of the
    /// two rates that each describe only half of it.
    /// </remarks>
    private static long DeriveEffectiveRate(long taxableBaseMinor, long taxMinor)
    {
        if (taxableBaseMinor <= 0L)
        {
            return 0L;
        }

        var rate = (decimal)taxMinor * OneHundredPercentMinor / taxableBaseMinor;

        return (long)decimal.Round(rate, 0, MidpointRounding.AwayFromZero);
    }
}
