namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// The document-type code that forms the third component of the IRN
/// (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// The wire values are NIC's three-letter codes, and they are part of a hash input — so the
/// <see cref="Code"/> mapping below is the single place the string form exists. The enum is
/// persisted by name (the model guard requires it), never by ordinal, so reordering these members
/// can never silently re-point stored rows.
/// </remarks>
public enum EinvoiceDocumentType
{
    /// <summary>Tax invoice — <c>INV</c>.</summary>
    TaxInvoice = 1,

    /// <summary>Credit note — <c>CRN</c>.</summary>
    CreditNote = 2,

    /// <summary>Debit note — <c>DBN</c>.</summary>
    DebitNote = 3,
}
