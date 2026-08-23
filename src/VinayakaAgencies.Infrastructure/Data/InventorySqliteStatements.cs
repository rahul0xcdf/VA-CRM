using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// SQLite statements for the Phase 2 inventory reports (phase-2-inventory.md Â§1).
/// </summary>
/// <remarks>
/// The same three invariants as <see cref="AccountingSqliteStatements"/>: every statement takes
/// <c>@CompanyId</c> first, aggregates in exact integer minor units (ADR-0003) and never re-derives
/// a value in SQL â€” <c>ValueMinor</c> was computed once, in the domain, when the row was born.
/// <para>
/// Quantities are stored as TEXT decimals by EF under SQLite, and <c>SUM()</c> over TEXT aggregates
/// in IEEE-754 double â€” the defect ADR-0003 exists to prevent. Every statement here therefore moves
/// quantities into integer space before aggregating: <c>CAST(ROUND(qty * 10000) AS INTEGER)</c>
/// yields minor quantities of 1/10,000, and the report-service mappers convert back to decimals
/// exactly once. Business dates are canonical calendar text (<c>yyyy-MM-dd</c>) so ranges compare
/// correctly as text; enums are stored by name, which is what the <c>Direction</c> literals match.
/// </para>
/// </remarks>
internal sealed class InventorySqliteStatements : ISqlStatementSource
{
    /// <summary>The shared expression lifting a TEXT decimal into an exact integer minor quantity.</summary>
    private static string Minor(string column) =>
        $"CAST(ROUND({column} * 10000) AS INTEGER)";

    public string Provider => DatabaseProviders.Sqlite;

    public IReadOnlyDictionary<SqlQueryKey, string> Statements { get; } =
        new Dictionary<SqlQueryKey, string>
        {
            // On-hand and value per item per godown. Value is summed signed-by-direction because
            // both increase and decrease rows carry positive ValueMinor columns. The CASTs are not
            // decoration: with an empty group set SQLite reports untyped aggregates as BLOB, and
            // Dapper then demands byte[] constructor parameters (ADR-0003 keeps the columns
            // INTEGER; this pins the read shape to match).
            [InventoryQueryKeys.StockOnHand] = $"""
                SELECT e.ItemId,
                       i.Name AS ItemName,
                       i.Code AS ItemCode,
                       e.GodownId,
                       g.Name AS GodownName,
                       CAST(SUM(e.QuantityInMinor) AS INTEGER) AS QuantityInMinor,
                       CAST(SUM(e.QuantityOutMinor) AS INTEGER) AS QuantityOutMinor,
                       CAST(SUM(e.QuantityInMinor)
                         - SUM(e.QuantityOutMinor) AS INTEGER) AS OnHandMinor,
                       CAST(SUM(CASE WHEN e.Direction = 'Increase'
                                THEN e.ValueMinor ELSE -e.ValueMinor END) AS INTEGER) AS ValueMinor
                FROM StockLedgerEntries e
                JOIN Items i ON i.Id = e.ItemId
                JOIN Godowns g ON g.Id = e.GodownId
                WHERE e.CompanyId = @CompanyId
                  AND e.EntryDate <= @AsOfDate
                  AND (@GodownId IS NULL OR e.GodownId = @GodownId)
                GROUP BY e.ItemId, i.Name, i.Code, e.GodownId, g.Name
                HAVING SUM(e.QuantityInMinor) <> 0
                    OR SUM(e.QuantityOutMinor) <> 0
                ORDER BY i.NameNormalised, g.NameNormalised;
                """,

            // Total stock value and quantity per item as of a date â€” the stock side of C2.1.
            [InventoryQueryKeys.StockValuation] = $"""
                SELECT e.ItemId,
                       i.Name AS ItemName,
                       CAST(SUM(e.QuantityInMinor)
                         - SUM(e.QuantityOutMinor) AS INTEGER) AS OnHandMinor,
                       CAST(SUM(CASE WHEN e.Direction = 'Increase'
                                THEN e.ValueMinor ELSE -e.ValueMinor END) AS INTEGER) AS ValueMinor
                FROM StockLedgerEntries e
                JOIN Items i ON i.Id = e.ItemId
                WHERE e.CompanyId = @CompanyId
                  AND e.EntryDate <= @AsOfDate
                GROUP BY e.ItemId, i.Name
                HAVING SUM(e.QuantityInMinor) <> 0
                    OR SUM(e.QuantityOutMinor) <> 0
                ORDER BY i.NameNormalised;
                """,

            // Items at or below reorder level with their current on-hand. Both sides compare in
            // integer minor quantities; zero-level items are absent (zero means no alerting).
            [InventoryQueryKeys.ReorderAlerts] = $"""
                SELECT i.Id AS ItemId,
                       i.Name AS ItemName,
                       i.Code AS ItemCode,
                       COALESCE(s.OnHandMinor, 0) AS OnHandMinor,
                       {Minor("i.ReorderLevel")} AS ReorderLevelMinor
                FROM Items i
                LEFT JOIN (
                    SELECT ItemId,
                           SUM(QuantityInMinor)
                             - SUM(QuantityOutMinor) AS OnHandMinor
                    FROM StockLedgerEntries
                    WHERE CompanyId = @CompanyId
                      AND EntryDate <= @AsOfDate
                    GROUP BY ItemId
                ) s ON s.ItemId = i.Id
                WHERE i.CompanyId = @CompanyId
                  AND i.IsActive = 1
                  AND COALESCE(s.OnHandMinor, 0)
                      <= {Minor("i.ReorderLevel")}
                  AND {Minor("i.ReorderLevel")} > 0
                ORDER BY COALESCE(s.OnHandMinor, 0), i.NameNormalised;
                """,

            // Every movement row up to the date, in posting order; the aging mapper replays FIFO
            // consumption over these and buckets the survivors. Filtered to one godown this yields
            // that godown's shelf alone.
            [InventoryQueryKeys.StockAgingLayers] = """
                SELECT e.GlobalSequence,
                       e.ItemId,
                       e.GodownId,
                       e.BatchNumber,
                       e.Direction,
                       e.EntryDate,
                       CASE WHEN e.Direction = 'Increase' THEN e.QuantityInMinor ELSE e.QuantityOutMinor END AS QuantityMinor,
                       e.UnitCostMinor
                FROM StockLedgerEntries e
                WHERE e.CompanyId = @CompanyId
                  AND e.EntryDate <= @AsOfDate
                  AND (@GodownId IS NULL OR e.GodownId = @GodownId)
                ORDER BY e.GlobalSequence;
                """,

            // The entry-screen lookup (C2.6): name / code / barcode over the whole catalogue,
            // resolved entirely inside SQLite with the indexes on Name/Code/Barcode. LIKE is
            // case-insensitive for ASCII on SQLite's default collation, which is how an operator
            // types anyway.
            [InventoryQueryKeys.ItemSearch] = """
                SELECT i.Id,
                       i.Name,
                       i.Code,
                       i.Barcode,
                       i.IsSerialTracked,
                       i.IsBatchTracked,
                       i.IsKit,
                       u.Code AS BaseUnitCode
                FROM Items i
                JOIN UnitsOfMeasure u ON u.Id = i.BaseUnitId
                WHERE i.CompanyId = @CompanyId
                  AND i.IsActive = 1
                  AND (@Search = ''
                       OR i.Name LIKE '%' || @Search || '%'
                       OR i.Code LIKE '%' || @Search || '%'
                       OR (@Search <> '' AND i.Barcode = @Search))
                ORDER BY i.NameNormalised
                LIMIT @Take;
                """,

            // One ledger's closing balance up to a date — the GL side of the C2.1 reconciliation.
            // Same current-revision predicate as every Phase 1 statement.
            [InventoryQueryKeys.ControlAccountBalance] = """
                SELECT COALESCE(SUM(jl.AmountMinor), 0) AS BalanceMinor
                FROM JournalLines jl
                JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                JOIN Ledgers l ON l.Id = jl.LedgerId
                WHERE jl.CompanyId = @CompanyId
                  AND vr.SupersededByRevisionId IS NULL
                  AND vr.VoucherDate <= @AsOfDate
                  AND l.CompanyId = @CompanyId
                  AND l.NameNormalised = @ControlLedgerName;
                """,
        };
}



