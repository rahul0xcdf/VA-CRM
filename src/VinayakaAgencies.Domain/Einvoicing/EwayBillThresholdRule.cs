namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Decides whether a consignment value requires an e-way bill (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// The threshold itself is deliberately <em>not</em> a constant here. Non-negotiable #6 puts rates
/// and thresholds in config rows, and this one has moved before and may move again — it lives on
/// <see cref="EinvoiceSettings.EwayBillThresholdMinor"/> and is passed in. What belongs in code is
/// only the comparison, including the detail that the statutory test is "exceeds", so a consignment
/// exactly at the threshold does not need a bill.
/// </remarks>
public static class EwayBillThresholdRule
{
    /// <summary>
    /// True when <paramref name="consignmentValueMinor"/> exceeds <paramref name="thresholdMinor"/>
    /// and an e-way bill is therefore required. Both are minor units of 1/10,000 (ADR-0003).
    /// </summary>
    public static bool IsRequired(long consignmentValueMinor, long thresholdMinor) =>
        consignmentValueMinor > thresholdMinor;
}
