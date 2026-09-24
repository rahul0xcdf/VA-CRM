namespace VinayakaAgencies.Domain.Interop;

/// <summary>
/// What a saved Excel column mapping brings in (phase-8-interoperability.md §2).
/// </summary>
/// <remarks>
/// An enum rather than a free string because the set is closed by what the importer can write, and a
/// mapping naming a target nothing handles would be a saved configuration that silently does nothing.
/// Stored by name, like every other enum in this schema (§5).
/// </remarks>
public enum ExcelImportTarget
{
    AccountGroups = 1,

    Ledgers = 2,

    Units = 3,

    StockGroups = 4,

    Godowns = 5,

    Items = 6,

    /// <summary>
    /// Vouchers, one row per ledger line.
    /// </summary>
    /// <remarks>
    /// Rows belonging to one voucher must be consecutive and share a voucher number. That is how a
    /// spreadsheet represents a document with several lines, and requiring it keeps the reader
    /// streaming — grouping rows that could appear anywhere in the sheet would mean holding the
    /// whole file, which §4a D2 rules out.
    /// </remarks>
    Vouchers = 7,
}
