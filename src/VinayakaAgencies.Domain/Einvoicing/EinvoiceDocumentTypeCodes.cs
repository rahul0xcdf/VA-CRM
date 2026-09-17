namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Translates <see cref="EinvoiceDocumentType"/> to and from NIC's three-letter wire codes.
/// </summary>
/// <remarks>
/// These three strings feed the IRN hash, so they live in exactly one place. A wrong code does not
/// fail loudly — it produces a valid-looking IRN that the portal rejects as not matching, which is
/// precisely the class of bug checkpoint C6.4 exists to catch.
/// </remarks>
public static class EinvoiceDocumentTypeCodes
{
    public const string TaxInvoice = "INV";
    public const string CreditNote = "CRN";
    public const string DebitNote = "DBN";

    /// <summary>The NIC wire code for <paramref name="documentType"/>.</summary>
    public static string Code(EinvoiceDocumentType documentType) => documentType switch
    {
        EinvoiceDocumentType.TaxInvoice => TaxInvoice,
        EinvoiceDocumentType.CreditNote => CreditNote,
        EinvoiceDocumentType.DebitNote => DebitNote,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Unknown e-invoice document type."),
    };

    /// <summary>Parses a NIC wire code back to its enum member.</summary>
    public static EinvoiceDocumentType Parse(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return code.Trim().ToUpperInvariant() switch
        {
            TaxInvoice => EinvoiceDocumentType.TaxInvoice,
            CreditNote => EinvoiceDocumentType.CreditNote,
            DebitNote => EinvoiceDocumentType.DebitNote,
            _ => throw new ArgumentOutOfRangeException(
                nameof(code), code, "Not a NIC e-invoice document-type code."),
        };
    }
}
