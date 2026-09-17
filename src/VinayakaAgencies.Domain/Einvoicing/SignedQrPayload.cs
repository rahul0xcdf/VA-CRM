namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// The ten mandatory fields carried in the signed QR code printed on a tax invoice
/// (phase-6-einvoice-eway.md §2, checkpoint C6.1).
/// </summary>
/// <remarks>
/// <para>
/// The QR actually printed is always the JWS blob NIC signs — we never synthesise a QR and present
/// it as registered. This type exists to hold the same ten fields computed from our own books so
/// the two can be compared: a mismatch means we sent the portal something other than what we
/// think we sent, and that is worth failing on before the invoice goes out the door.
/// </para>
/// <para>
/// <see cref="TotalInvoiceValueMinor"/> stays in minor units (ADR-0003) and is converted to decimal
/// once, at the JSON boundary. <see cref="MainHsnCode"/> is the HSN of the line carrying the
/// highest taxable value — not the first line, which is a common and silent mistake.
/// </para>
/// </remarks>
/// <param name="SellerGstin">Supplier GSTIN.</param>
/// <param name="BuyerGstin">Recipient GSTIN, or <c>URP</c> for an unregistered party.</param>
/// <param name="DocumentNumber">Invoice number exactly as issued.</param>
/// <param name="DocumentTypeCode">NIC three-letter code — <c>INV</c> / <c>CRN</c> / <c>DBN</c>.</param>
/// <param name="DocumentDate">Invoice date; rendered <c>dd/MM/yyyy</c> on the wire.</param>
/// <param name="TotalInvoiceValueMinor">Invoice value in minor units of 1/10,000.</param>
/// <param name="ItemCount">Number of line items.</param>
/// <param name="MainHsnCode">HSN of the highest-taxable-value line.</param>
/// <param name="Irn">The 64-character IRN.</param>
/// <param name="IrnDateText">The portal's IRN generation timestamp, verbatim as NIC returned it.</param>
public sealed record SignedQrPayload(
    string SellerGstin,
    string BuyerGstin,
    string DocumentNumber,
    string DocumentTypeCode,
    DateOnly DocumentDate,
    long TotalInvoiceValueMinor,
    int ItemCount,
    string MainHsnCode,
    string Irn,
    string IrnDateText)
{
    /// <summary>What NIC puts in the QR for a party with no GSTIN.</summary>
    public const string UnregisteredParty = "URP";
}
