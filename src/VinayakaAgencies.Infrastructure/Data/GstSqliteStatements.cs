using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// SQLite statements for the Phase 5 GST returns (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Every statement reads only posted invoices — <c>Status = 'Issued'</c>, the state only the journal
/// engine's posting can produce — and only the tax snapshots the posting flow wrote, so a return can
/// never disagree with the books by re-deriving anything (checkpoint C5.3). Aggregations happen in
/// the application layer over integer minor units (ADR-0003); these statements stay row-shaped.
/// Enum columns are stored by name (§5), which is what the <c>Kind</c>/<c>Status</c> literals match.
/// </remarks>
internal sealed class GstSqliteStatements : ISqlStatementSource
{
    public string Provider => DatabaseProviders.Sqlite;

    /// <summary>
    /// Shared projection: one posted invoice line with its document header facts resolved — party
    /// GSTIN (document override first, customer master second), place of supply (document snapshot,
    /// customer master, then the company's own state) and the item's HSN code.
    /// </summary>
    private const string InvoiceLineSelect = """
        SELECT td.Id AS DocumentId,
               td.Number AS DocumentNumber,
               td.DocumentDate AS VoucherDate,
               COALESCE(NULLIF(td.PartyGstin, ''), c.Gstin) AS PartyGstin,
               COALESCE(td.PartyReferenceNumber, '') AS PartyReferenceNumber,
               COALESCE(
                   NULLIF(td.PlaceOfSupplyStateCode, ''),
                   NULLIF(c.PlaceOfSupplyStateCode, ''),
                   co.StateCode) AS PlaceOfSupplyStateCode,
               td.IsReverseCharge AS IsReverseCharge,
               tdl.ItemId AS ItemId,
               h.Code AS HsnCode,
               h.Description AS HsnDescription,
               tdl.QuantityMinor AS QuantityMinor,
               tdl.TaxableValueMinor AS TaxableValueMinor,
               tdl.GstRatePercentMinor AS RatePercentMinor,
               tdl.IgstMinor AS IgstMinor,
               tdl.CgstMinor AS CgstMinor,
               tdl.SgstMinor AS SgstMinor,
               tdl.CessMinor AS CessMinor
        FROM TradeDocuments td
        JOIN TradeDocumentLines tdl ON tdl.TradeDocumentId = td.Id
        LEFT JOIN Customers c ON c.CompanyId = td.CompanyId AND c.LedgerId = td.PartyLedgerId
        LEFT JOIN Companies co ON co.Id = td.CompanyId
        LEFT JOIN Items i ON i.Id = tdl.ItemId
        LEFT JOIN HsnSac h ON h.Id = i.HsnSacId
        WHERE td.CompanyId = @CompanyId
          AND td.Status = 'Issued'
          AND td.DocumentDate >= @FromDate
          AND td.DocumentDate <= @ToDate
        """;

    public IReadOnlyDictionary<SqlQueryKey, string> Statements { get; } =
        new Dictionary<SqlQueryKey, string>
        {
            [GstQueryKeys.SalesInvoiceLinesForPeriod] =
                $"""{InvoiceLineSelect} AND td.Kind = 'TaxInvoice' ORDER BY td.DocumentDate, td.Number;""",

            [GstQueryKeys.PurchaseInvoiceLinesForPeriod] =
                $"""{InvoiceLineSelect} AND td.Kind = 'PurchaseInvoice' ORDER BY td.DocumentDate, td.Number;""",

            // Documents-issued summary: every invoice raised in the window bucketed by status, so
            // cancellations show where the portal expects them.
            [GstQueryKeys.TaxInvoiceStatusCountsForPeriod] = """
                SELECT td.Status AS Status,
                       COUNT(*) AS Count
                FROM TradeDocuments td
                WHERE td.CompanyId = @CompanyId
                  AND td.Kind = 'TaxInvoice'
                  AND td.DocumentDate >= @FromDate
                  AND td.DocumentDate <= @ToDate
                GROUP BY td.Status;
                """,

            [GstQueryKeys.SalesInvoiceNumbersForPeriod] = """
                SELECT td.Number AS Number
                FROM TradeDocuments td
                WHERE td.CompanyId = @CompanyId
                  AND td.Kind = 'TaxInvoice'
                  AND td.Number IS NOT NULL
                  AND td.DocumentDate >= @FromDate
                  AND td.DocumentDate <= @ToDate
                ORDER BY td.DocumentDate, td.Number;
                """,
        };
}
