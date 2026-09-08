using System.Globalization;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Gst;

/// <summary>
/// The GST arithmetic conventions, in one place: tax on a taxable value at a percentage rate, and
/// the per-invoice rupee round-off (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// All inputs and outputs are integer minor units of 1/10,000 (ADR-0003). The intermediate product
/// is taken through <see cref="decimal"/> — exact for every representable invoice — and rounded
/// once, away from zero, which is the Indian tax convention; nothing here ever works in binary
/// floating point. Intra-state CGST/SGST splitting keeps the two halves summing to the computed
/// total exactly: an odd paisa lands on SGST rather than being lost.
/// </remarks>
public static class GstTaxMath
{
    private const decimal RateScale = 1_000_000m;

    /// <summary>
    /// Tax on a taxable value at a percentage rate: <c>taxable × rate% / 100</c>, rounded to the
    /// nearest minor unit, half away from zero.
    /// </summary>
    /// <param name="taxableValueMinor">Taxable amount in minor units.</param>
    /// <param name="ratePercentMinor">Rate as a percent in minor units (18% = 180_000).</param>
    public static long TaxMinor(long taxableValueMinor, long ratePercentMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taxableValueMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(ratePercentMinor);

        if (ratePercentMinor == 0L)
        {
            return 0L;
        }

        // decimal operands: a large taxable value times the rate scale would overflow a long.
        var raw = (decimal)taxableValueMinor * ratePercentMinor / RateScale;
        return (long)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    /// <summary>Splits a total tax into equal CGST/SGST halves, any odd minor unit on SGST.</summary>
    public static (long CgstMinor, long SgstMinor) SplitHalf(long totalTaxMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalTaxMinor);

        var cgst = totalTaxMinor / 2L;
        return (cgst, totalTaxMinor - cgst);
    }

    /// <summary>
    /// The rupee round-off difference for an invoice grand total: how much must be added to
    /// (positive) or subtracted from (negative) the exact total so it lands on the nearest rupee.
    /// </summary>
    public static long RupeeRoundOffMinor(long totalMinor) =>
        (long)Math.Round(
            totalMinor / (decimal)MoneyScale.UnitsPerMajor,
            0,
            MidpointRounding.AwayFromZero)
        * MoneyScale.UnitsPerMajor
        - totalMinor;

    /// <summary>Formats a minor-unit amount for diagnostics, invariant of locale.</summary>
    public static string ToInvariantString(long minor) =>
        MoneyScale.FromMinor(minor).ToString("F4", CultureInfo.InvariantCulture);
}
