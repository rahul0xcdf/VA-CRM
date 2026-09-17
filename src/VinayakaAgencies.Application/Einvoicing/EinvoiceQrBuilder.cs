using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Assembles the ten signed-QR fields from our own books, so they can be checked against what the
/// portal signed (phase-6-einvoice-eway.md §2, checkpoint C6.1).
/// </summary>
/// <remarks>
/// Pure, and deliberately so: the one field here that is easy to get wrong is
/// <c>MainHsnCode</c>, which is the HSN of the line carrying the <em>highest taxable value</em> and
/// not the first line. That is a one-line mistake with no visible symptom, so it gets a direct test
/// rather than a comment promising it was thought about.
/// </remarks>
public static class EinvoiceQrBuilder
{
    /// <summary>Builds the ten fields for a document the portal has acknowledged.</summary>
    public static SignedQrPayload Build(
        EinvoiceRequest request,
        string irn,
        string irnDateText)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(irn);
        ArgumentException.ThrowIfNullOrWhiteSpace(irnDateText);

        if (request.Lines.Count is 0)
        {
            throw new InvalidOperationException(
                "An e-invoice must carry at least one line before a QR can describe it.");
        }

        return new SignedQrPayload(
            request.Seller.Gstin,
            request.Buyer.Gstin,
            request.DocumentNumber,
            EinvoiceDocumentTypeCodes.Code(request.DocumentType),
            request.DocumentDate,
            TotalInvoiceValueMinor(request),
            request.Lines.Count,
            MainHsnCode(request.Lines),
            irn,
            irnDateText);
    }

    /// <summary>
    /// The HSN of the highest-taxable-value line. Ties fall to the first such line, which keeps the
    /// result stable for a given invoice rather than depending on enumeration order.
    /// </summary>
    public static string MainHsnCode(IReadOnlyList<EinvoiceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count is 0)
        {
            throw new ArgumentException("An invoice has at least one line.", nameof(lines));
        }

        var main = lines[0];

        foreach (var line in lines)
        {
            if (line.TaxableValueMinor > main.TaxableValueMinor)
            {
                main = line;
            }
        }

        return main.HsnCode;
    }

    /// <summary>
    /// Invoice value as the portal states it: every line total plus the invoice's rounding
    /// difference. The round-off is included because the portal compares this against the figure in
    /// the payload, and Phase 5 already posted that difference to a ledger rather than dropping it.
    /// </summary>
    public static long TotalInvoiceValueMinor(EinvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Lines.Sum(line => line.TotalValueMinor) + request.RoundOffMinor;
    }
}
