namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// One bank-ledger line as it appears on the reconciliation statement (spec §12).
/// </summary>
public sealed record BankReconciliationLine(
    Guid JournalLineId,
    DateOnly VoucherDate,
    string VoucherNumber,
    string VoucherTypeName,
    decimal Debit,
    decimal Credit,
    string? Narration,
    bool IsCleared,
    DateOnly? ClearedOnBank);

/// <summary>
/// Manual bank reconciliation: marks a posted line as cleared by the bank, or withdraws the mark.
/// </summary>
/// <remarks>
/// Marks are operational metadata, not financial content — the journal is insert-only (ADR-0002), so
/// clearing state lives in its own table where it may be added, changed and removed freely, with
/// every one of those changes edit-logged like any other. Statement import and auto-matching belong
/// to Phase 7; this phase is deliberately manual.
/// </remarks>
public interface IBankReconciliationService
{
    Task<IReadOnlyList<BankReconciliationLine>> GetStatementAsync(
        Guid ledgerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>Marks one journal line as having cleared the bank on <paramref name="bankDate"/>.</summary>
    Task MarkClearedAsync(
        Guid journalLineId,
        DateOnly bankDate,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws a mark — an entered date was wrong, or the item bounced.</summary>
    Task UnmarkClearedAsync(
        Guid journalLineId,
        CancellationToken cancellationToken = default);
}
