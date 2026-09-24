using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// SQLite statements for the Phase 8 import reconciliation (phase-8-interoperability.md C8.6).
/// </summary>
/// <remarks>
/// The same three invariants the accounting statements hold to: the current-revision predicate, so
/// an altered voucher is counted once; <c>SUM</c> over the integer <c>AmountMinor</c> column, so the
/// totals are exact (ADR-0003); and <c>@CompanyId</c> first.
/// </remarks>
internal sealed class InteropSqliteStatements : ISqlStatementSource
{
    public string Provider => DatabaseProviders.Sqlite;

    public IReadOnlyDictionary<SqlQueryKey, string> Statements { get; } =
        new Dictionary<SqlQueryKey, string>
        {
            // Whole-life debit and credit totals. COALESCE because a company with no postings at all
            // must read as zero rather than as null — the reconciliation runs on a fresh install, and
            // that is exactly the case a migration starts from.
            [InteropQueryKeys.JournalTotals] = """
                SELECT COALESCE(SUM(CASE WHEN jl.AmountMinor > 0 THEN jl.AmountMinor ELSE 0 END), 0) AS DebitMinor,
                       COALESCE(SUM(CASE WHEN jl.AmountMinor < 0 THEN -jl.AmountMinor ELSE 0 END), 0) AS CreditMinor
                FROM JournalLines jl
                JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                WHERE jl.CompanyId = @CompanyId
                  AND vr.SupersededByRevisionId IS NULL;
                """,
        };
}
