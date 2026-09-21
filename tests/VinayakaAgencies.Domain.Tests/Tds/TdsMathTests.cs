using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Domain.Tests.Tds;

/// <summary>
/// The TDS/TCS arithmetic, against hand-computed figures (phase-7-tds-payroll-banking.md,
/// checkpoints C7.3 and C7.4).
/// </summary>
/// <remarks>
/// Every rate and threshold in these tests is passed in as an argument, exactly as it is at runtime,
/// where it comes from a data row. The fixtures use Section 194C's real figures — 1% and 2%, ₹30,000
/// single and ₹1,00,000 annual — because a rule tested only against round invented numbers tends to
/// be a rule that was never checked against the statute it implements. None of them are in
/// <c>src/</c>; they are here.
/// </remarks>
public class TdsMathTests
{
    private const long OnePercent = 10_000L;
    private const long TwoPercent = 20_000L;
    private const long TwentyPercent = 200_000L;

    private static long Rupees(decimal amount) => MoneyScale.ToMinor(amount);

    [Fact]
    public void Compute_BelowBothThresholds_DeductsNothingAndSaysWhy()
    {
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(20_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.HasDeduction.ShouldBeFalse();
        outcome.DeductedAmountMinor.ShouldBe(0L);
        outcome.Reason.ShouldBe(NoDeductionReason.BelowThreshold);

        // The running total still advances — the payment counts toward the year even though it was
        // not taxed, which is the whole basis of the annual test.
        outcome.CumulativeBaseAfterMinor.ShouldBe(Rupees(20_000m));
    }

    [Fact]
    public void Compute_SinglePaymentOverTheSingleThreshold_TaxesThatPaymentAlone()
    {
        // One ₹50,000 bill in a year that is otherwise quiet. 1% of 50,000 = ₹500.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(50_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.TaxableBaseMinor.ShouldBe(Rupees(50_000m));
        outcome.DeductedAmountMinor.ShouldBe(Rupees(500m));
        outcome.IsThresholdCrossingPayment.ShouldBeFalse();
    }

    [Fact]
    public void Compute_SinglePaymentTest_DoesNotReopenEarlierSmallerPayments()
    {
        // ₹40,000 already paid across small untaxed bills, now one ₹50,000 bill. The single-payment
        // test taxes *that payment*; it does not retrospectively tax the earlier ₹40,000, because the
        // year's total (₹90,000) is still under the annual limit.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(50_000m),
            cumulativeBaseBeforeMinor: Rupees(40_000m),
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.TaxableBaseMinor.ShouldBe(Rupees(50_000m));
        outcome.DeductedAmountMinor.ShouldBe(Rupees(500m));
    }

    [Fact]
    public void Compute_ThePaymentThatCrossesTheAnnualLimit_CatchesUpOnEverythingUntaxed()
    {
        // C7.3, the case that matters. Five untaxed ₹20,000 bills (₹1,00,000 exactly — not *over*
        // the limit, so nothing was due). A sixth ₹20,000 bill takes the year to ₹1,20,000, and the
        // whole ₹1,20,000 becomes taxable at once: 1% = ₹1,200, not ₹200 on this bill alone.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(20_000m),
            cumulativeBaseBeforeMinor: Rupees(100_000m),
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.TaxableBaseMinor.ShouldBe(Rupees(120_000m));
        outcome.DeductedAmountMinor.ShouldBe(Rupees(1_200m));
        outcome.IsThresholdCrossingPayment.ShouldBeTrue();
        outcome.CumulativeBaseAfterMinor.ShouldBe(Rupees(120_000m));
    }

    [Fact]
    public void Compute_AtExactlyTheAnnualLimit_DeductsNothing()
    {
        // The statute says "exceeds", so landing exactly on the limit is not crossing it. An
        // off-by-one here would deduct a year early on every payee who lands on a round number.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(20_000m),
            cumulativeBaseBeforeMinor: Rupees(80_000m),
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.HasDeduction.ShouldBeFalse();
        outcome.CumulativeBaseAfterMinor.ShouldBe(Rupees(100_000m));
    }

    [Fact]
    public void Compute_AfterTheCrossing_TaxesOnlyTheNewPayment()
    {
        // The year is already taxable and ₹1,20,000 has been taxed. The next ₹20,000 bill is taxed on
        // itself only — catching up twice would double-deduct.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(20_000m),
            cumulativeBaseBeforeMinor: Rupees(120_000m),
            cumulativeTaxedBaseBeforeMinor: Rupees(120_000m),
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.TaxableBaseMinor.ShouldBe(Rupees(20_000m));
        outcome.DeductedAmountMinor.ShouldBe(Rupees(200m));
        outcome.IsThresholdCrossingPayment.ShouldBeFalse();
    }

    [Fact]
    public void Compute_WalkingAWholeYear_TaxesEachRupeeExactlyOnce()
    {
        // The property the two threshold rules exist to preserve: however the payments fall, the
        // year's tax equals the rate applied to the year's total once the limit is passed.
        long cumulative = 0L;
        long taxed = 0L;
        long totalTax = 0L;

        foreach (var payment in new[] { 20_000m, 20_000m, 20_000m, 20_000m, 20_000m, 20_000m, 15_000m })
        {
            var outcome = TdsMath.Compute(
                baseAmountMinor: Rupees(payment),
                cumulativeBaseBeforeMinor: cumulative,
                cumulativeTaxedBaseBeforeMinor: taxed,
                ratePercentMinor: OnePercent,
                singleTransactionThresholdMinor: Rupees(30_000m),
                annualThresholdMinor: Rupees(100_000m));

            cumulative = outcome.CumulativeBaseAfterMinor;
            taxed += outcome.TaxableBaseMinor;
            totalTax += outcome.DeductedAmountMinor;
        }

        cumulative.ShouldBe(Rupees(135_000m));
        taxed.ShouldBe(Rupees(135_000m), "every rupee of a taxable year must be taxed exactly once");
        totalTax.ShouldBe(Rupees(1_350m));
    }

    [Fact]
    public void Compute_WithNoThresholdsConfigured_DeductsFromTheFirstRupee()
    {
        // How 194Q and 206C(1H) behave once their applicability flag is on: no free allowance.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(10_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: 1_000L,
            singleTransactionThresholdMinor: 0L,
            annualThresholdMinor: 0L);

        outcome.TaxableBaseMinor.ShouldBe(Rupees(10_000m));
        outcome.DeductedAmountMinor.ShouldBe(Rupees(10m));
    }

    [Fact]
    public void Compute_AtTheHigherNoPanRate_UsesWhateverRateItIsGiven()
    {
        // Section 206AA. The caller decides which rate to pass; this only proves the higher one is
        // honoured, because the rate itself is a data row and not this file's business.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(50_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: TwentyPercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m));

        outcome.DeductedAmountMinor.ShouldBe(Rupees(10_000m));
    }

    [Fact]
    public void Compute_UnderANilCertificate_DeductsNothingButStillRecordsTheBase()
    {
        // "Nothing was deducted because a certificate said so" is a different fact from "nothing was
        // deducted because nobody configured the section", and the return has to show the first.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(50_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: TwoPercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: Rupees(100_000m),
            certificateRatePercentMinor: 0L);

        outcome.DeductedAmountMinor.ShouldBe(0L);
        outcome.TaxableBaseMinor.ShouldBe(Rupees(50_000m));
        outcome.IsCertificateApplied.ShouldBeTrue();
        outcome.Reason.ShouldBe(NoDeductionReason.NilCertificate);
    }

    [Fact]
    public void Compute_UnderAnUncappedLowerCertificate_UsesTheCertificateRate()
    {
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(100_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: TwoPercent,
            singleTransactionThresholdMinor: Rupees(30_000m),
            annualThresholdMinor: 0L,
            certificateRatePercentMinor: 5_000L);

        outcome.DeductedAmountMinor.ShouldBe(Rupees(500m));
        outcome.IsCertificateApplied.ShouldBeTrue();
        outcome.EffectiveRatePercentMinor.ShouldBe(5_000L);
    }

    [Fact]
    public void Compute_WhereACappedCertificateRunsOutPartway_SplitsTheBaseAcrossBothRates()
    {
        // A certificate for 0.5% "up to ₹60,000" against a ₹100,000 payment taxed at 2% normally:
        // 60,000 at 0.5% = ₹300, plus 40,000 at 2% = ₹800. Total ₹1,100.
        // Treating the whole payment as certificate-rated would claim ₹500 and under-deduct;
        // ignoring the certificate would deduct ₹2,000 and over-deduct. Both are wrong on a return.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(100_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: TwoPercent,
            singleTransactionThresholdMinor: 0L,
            annualThresholdMinor: 0L,
            certificateRatePercentMinor: 5_000L,
            certificateRoomMinor: Rupees(60_000m));

        outcome.DeductedAmountMinor.ShouldBe(Rupees(1_100m));
        outcome.IsCertificateApplied.ShouldBeTrue();

        // The reported rate is the blend actually applied, not either component.
        outcome.EffectiveRatePercentMinor.ShouldBe(11_000L);
    }

    [Fact]
    public void Compute_WithACertificateAlreadyExhausted_FallsBackToTheScheduledRate()
    {
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(50_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: TwoPercent,
            singleTransactionThresholdMinor: 0L,
            annualThresholdMinor: 0L,
            certificateRatePercentMinor: 5_000L,
            certificateRoomMinor: 0L);

        outcome.DeductedAmountMinor.ShouldBe(Rupees(1_000m));
        outcome.IsCertificateApplied.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1_234.40, 12.00)]
    [InlineData(1_250.00, 13.00)]
    [InlineData(1_249.99, 12.00)]
    public void Compute_RoundsTaxToWholeRupees(decimal baseAmount, decimal expectedTax)
    {
        // TDS is deposited and challaned in whole rupees, so the books must carry the figure that
        // will actually be paid. Paise left in the ledger would put the liability account and the
        // challan permanently out of step.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(baseAmount),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: 0L,
            annualThresholdMinor: 0L);

        outcome.DeductedAmountMinor.ShouldBe(Rupees(expectedTax));
    }

    [Fact]
    public void RoundToRupee_RoundsHalvesAwayFromZero()
    {
        TdsMath.RoundToRupee(Rupees(10.5m)).ShouldBe(Rupees(11m));
        TdsMath.RoundToRupee(Rupees(10.4999m)).ShouldBe(Rupees(10m));
        TdsMath.RoundToRupee(Rupees(0m)).ShouldBe(0L);
    }

    [Fact]
    public void Compute_OnAVeryLargePayment_DoesNotOverflow()
    {
        // base × rate overflows 64 bits long before the divide brings it back, which is why the
        // multiply is done in decimal. ₹50 crore at 2% is ₹1 crore.
        var outcome = TdsMath.Compute(
            baseAmountMinor: Rupees(500_000_000m),
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: TwoPercent,
            singleTransactionThresholdMinor: 0L,
            annualThresholdMinor: 0L);

        outcome.DeductedAmountMinor.ShouldBe(Rupees(10_000_000m));
    }

    [Fact]
    public void Compute_RefusesNegativeInput()
    {
        // A negative payment is a defect upstream, not a credit note. Absorbing it would post a
        // negative deduction onto a return.
        Should.Throw<ArgumentOutOfRangeException>(() => TdsMath.Compute(
            baseAmountMinor: -1L,
            cumulativeBaseBeforeMinor: 0L,
            cumulativeTaxedBaseBeforeMinor: 0L,
            ratePercentMinor: OnePercent,
            singleTransactionThresholdMinor: 0L,
            annualThresholdMinor: 0L));
    }
}
