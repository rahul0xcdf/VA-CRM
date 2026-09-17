using System.Security.Cryptography;
using System.Text;

namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Computes the Invoice Reference Number exactly as the NIC portal computes it
/// (phase-6-einvoice-eway.md §2, checkpoint C6.4).
/// </summary>
/// <remarks>
/// <para>
/// IRN = SHA-256 over <c>SupplierGSTIN + FY + DocType + DocNumber</c>, concatenated in that order
/// with <b>no separators</b>, rendered as 64 lowercase hex characters. NIC's published worked
/// example is the regression fixture: GSTIN <c>01AAAAA9999A19N</c>, an invoice <c>ABC01234</c> dated
/// 12 December 2019, hashes the string <c>01AAAAA9999A19N2019-20INVABC01234</c>.
/// </para>
/// <para>
/// The order matters and is the whole reason this lives in one pure, directly-tested function: get
/// it wrong and every IRN is a well-formed 64-character string that the portal simply disagrees
/// with. Nothing here talks to the network, so the computation is verifiable without credentials —
/// which is what lets C6.4 be met while <c>open-questions.md</c> #10 is still open.
/// </para>
/// <para>
/// The IRN is also our idempotency key. Because it is a pure function of four values we already
/// hold, a retry after a timeout recomputes the same IRN rather than inventing a second one
/// (checkpoint C6.5).
/// </para>
/// </remarks>
public static class IrnFormula
{
    /// <summary>The number of hex characters in an IRN.</summary>
    public const int Length = 64;

    /// <summary>
    /// The exact string that gets hashed. Exposed because a mismatch between our IRN and NIC's is
    /// diagnosed by comparing this, not the hash.
    /// </summary>
    public static string HashInput(
        string supplierGstin,
        DateOnly documentDate,
        EinvoiceDocumentType documentType,
        string documentNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierGstin);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        return string.Concat(
            supplierGstin.Trim().ToUpperInvariant(),
            GstFinancialYear.For(documentDate),
            EinvoiceDocumentTypeCodes.Code(documentType),
            documentNumber.Trim());
    }

    /// <summary>The 64-character lowercase-hex IRN for one document.</summary>
    public static string Compute(
        string supplierGstin,
        DateOnly documentDate,
        EinvoiceDocumentType documentType,
        string documentNumber) =>
        Hash(HashInput(supplierGstin, documentDate, documentType, documentNumber));

    /// <summary>SHA-256 of <paramref name="hashInput"/> as 64 lowercase hex characters.</summary>
    public static string Hash(string hashInput)
    {
        ArgumentNullException.ThrowIfNull(hashInput);

        // ASCII is deliberate rather than UTF-8: every character NIC allows in a GSTIN, an FY
        // label, a doc-type code or a document number is ASCII, and a non-ASCII character smuggled
        // into a document number would otherwise hash to something the portal cannot reproduce.
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(hashInput));

        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>True when <paramref name="irn"/> is shaped like an IRN (64 lowercase hex characters).</summary>
    public static bool IsWellFormed(string? irn) =>
        irn is { Length: Length } && irn.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
