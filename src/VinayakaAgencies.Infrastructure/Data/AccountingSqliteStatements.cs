using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// SQLite statements for the Phase 1 accounting reports (spec §8.4).
/// </summary>
/// <remarks>
/// Three invariants hold across every statement here:
/// <list type="number">
/// <item>Every read goes through the shared current-revision predicate —
/// <c>SupersededByRevisionId IS NULL</c> (spec §8.2). A statement that forgets it double-counts
/// every altered voucher, silently, in a figure nobody recomputes by hand.</item>
/// <item>Every aggregation is <c>SUM</c> over the integer <c>AmountMinor</c> column (ADR-0003), so
/// totals are exact integer arithmetic and the trial-balance invariant holds without tolerance.</item>
/// <item>Every company-facing statement takes <c>@CompanyId</c> as its first parameter (spec §10).</item>
/// </list>
/// Business dates are stored as canonical calendar text (<c>yyyy-MM-dd</c>) by EF Core, so date
/// ranges compare correctly as text. Enum columns are stored by name (§5), which is what the
/// <c>Nature</c> and <c>Status</c> literals below match against.
/// </remarks>
internal sealed class AccountingSqliteStatements : ISqlStatementSource
{
    public string Provider => DatabaseProviders.Sqlite;

    /// <summary>The shared predicate for "this revision is the voucher's current state" (§8.2).</summary>
    private const string CurrentRevision = "vr.SupersededByRevisionId IS NULL";

    public IReadOnlyDictionary<SqlQueryKey, string> Statements { get; } =
        new Dictionary<SqlQueryKey, string>
        {
            // Closing balance per ledger up to a date.
            [AccountingQueryKeys.LedgerBalancesAsOf] = $"""
                SELECT l.Id AS LedgerId,
                       l.Name AS LedgerName,
                       g.Name AS AccountGroupName,
                       g.Nature AS Nature,
                       COALESCE(p.BalanceMinor, 0) AS ClosingMinor
                FROM Ledgers l
                JOIN AccountGroups g ON g.Id = l.AccountGroupId
                LEFT JOIN (
                    SELECT jl.LedgerId AS Lid,
                           SUM(jl.AmountMinor) AS BalanceMinor
                    FROM JournalLines jl
                    JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    WHERE jl.CompanyId = @CompanyId
                      AND {CurrentRevision}
                      AND vr.VoucherDate <= @AsOfDate
                    GROUP BY jl.LedgerId
                ) p ON p.Lid = l.Id
                WHERE l.CompanyId = @CompanyId
                ORDER BY g.MaterialisedPath, l.NameNormalised;
                """,

            // Debit and credit movement per ledger between two dates.
            [AccountingQueryKeys.LedgerBalancesForPeriod] = """
                SELECT l.Id AS LedgerId,
                       l.Name AS LedgerName,
                       g.Name AS AccountGroupName,
                       g.Nature AS Nature,
                       COALESCE(m.DebitMinor, 0) AS DebitMinor,
                       COALESCE(m.CreditMinor, 0) AS CreditMinor
                FROM Ledgers l
                JOIN AccountGroups g ON g.Id = l.AccountGroupId
                LEFT JOIN (
                    SELECT jl.LedgerId AS Lid,
                           SUM(CASE WHEN jl.AmountMinor > 0 THEN jl.AmountMinor ELSE 0 END) AS DebitMinor,
                           SUM(CASE WHEN jl.AmountMinor < 0 THEN -jl.AmountMinor ELSE 0 END) AS CreditMinor
                    FROM JournalLines jl
                    JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    WHERE jl.CompanyId = @CompanyId
                      AND vr.SupersededByRevisionId IS NULL
                      AND vr.VoucherDate >= @FromDate
                      AND vr.VoucherDate <= @ToDate
                    GROUP BY jl.LedgerId
                ) m ON m.Lid = l.Id
                WHERE l.CompanyId = @CompanyId
                ORDER BY g.MaterialisedPath, l.NameNormalised;
                """,

            // Trial balance: one net position per ledger between the financial-year start and the
            // as-on date; the caller splits the closing figure into Dr/Cr presentation columns by
            // sign (§8.4). Ordered along the chart of accounts so the grid reads like Tally's.
            [AccountingQueryKeys.TrialBalance] = """
                WITH Movement AS (
                    SELECT jl.LedgerId AS Lid,
                           SUM(jl.AmountMinor) AS NetMinor
                    FROM JournalLines jl
                    JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    WHERE jl.CompanyId = @CompanyId
                      AND vr.SupersededByRevisionId IS NULL
                      AND vr.VoucherDate >= @FromDate
                      AND vr.VoucherDate <= @ToDate
                    GROUP BY jl.LedgerId
                )
                SELECT l.Id AS LedgerId,
                       l.Name AS LedgerName,
                       g.Name AS AccountGroupName,
                       g.Nature AS Nature,
                       COALESCE(m.NetMinor, 0) AS ClosingMinor
                FROM Ledgers l
                JOIN AccountGroups g ON g.Id = l.AccountGroupId
                LEFT JOIN Movement m ON m.Lid = l.Id
                WHERE l.CompanyId = @CompanyId
                ORDER BY g.MaterialisedPath, l.NameNormalised;
                """,

            // Group roll-up via the materialised-path prefix (§4.2). The prefix test is a bounded
            // substr comparison rather than LIKE so it is exact regardless of collation; the
            // (CompanyId, MaterialisedPath) index keeps it an index range scan.
            [AccountingQueryKeys.GroupBalancesAsOf] = """
                SELECT gg.Id AS GroupId,
                       gg.Name AS GroupName,
                       gg.Nature AS Nature,
                       COALESCE((
                            SELECT SUM(jl.AmountMinor)
                            FROM JournalLines jl
                            JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                            JOIN Ledgers l ON l.Id = jl.LedgerId
                            JOIN AccountGroups g ON g.Id = l.AccountGroupId
                            WHERE jl.CompanyId = @CompanyId
                              AND vr.SupersededByRevisionId IS NULL
                              AND vr.VoucherDate <= @AsOfDate
                              AND substr(g.MaterialisedPath, 1, length(gg.MaterialisedPath))
                                  = gg.MaterialisedPath
                       ), 0) AS BalanceMinor
                FROM AccountGroups gg
                WHERE gg.CompanyId = @CompanyId
                ORDER BY gg.MaterialisedPath;
                """,

            // Income and expense movement between two dates, still credit-positive-signed; the
            // service flips incomes positive. AffectsGrossProfit drives the gross block (§8.4).
            [AccountingQueryKeys.ProfitAndLossForPeriod] = """
                WITH Movement AS (
                    SELECT jl.LedgerId AS Lid,
                           SUM(jl.AmountMinor) AS NetMinor
                    FROM JournalLines jl
                    JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    WHERE jl.CompanyId = @CompanyId
                      AND vr.SupersededByRevisionId IS NULL
                      AND vr.VoucherDate >= @FromDate
                      AND vr.VoucherDate <= @ToDate
                    GROUP BY jl.LedgerId
                )
                SELECT l.Id AS LedgerId,
                       l.Name AS LedgerName,
                       g.Name AS AccountGroupName,
                       g.Nature AS Nature,
                       g.AffectsGrossProfit AS AffectsGrossProfit,
                       COALESCE(m.NetMinor, 0) AS NetMinor
                FROM Movement m
                JOIN Ledgers l ON l.Id = m.Lid
                JOIN AccountGroups g ON g.Id = l.AccountGroupId
                WHERE g.Nature IN ('Income', 'Expense')
                ORDER BY g.AffectsGrossProfit DESC, g.Nature DESC, l.NameNormalised;
                """,

            // Asset and liability positions as of a date, plus the derived Profit & Loss A/c line.
            //
            // The journal has no year-end closing posting (§8.3 — carry-forward is derived), so the
            // carried profit is accumulated income less expense of ALL time up to the date: only
            // then does Assets = Liabilities + equity hold across financial years (C1.6).
            // @FinancialYearStartDate stays part of the key contract but is not referenced yet.
            // Amounts leave sign-normalised — assets debit-positive, liabilities credit-positive —
            // and the synthetic P&L row lands on the side its sign puts it: profit under Capital,
            // a loss shown on the asset side, exactly as Tally presents it.
            [AccountingQueryKeys.BalanceSheetAsOf] = """
                WITH Position AS (
                    SELECT jl.LedgerId AS Lid,
                           SUM(jl.AmountMinor) AS BalanceMinor
                    FROM JournalLines jl
                    JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    WHERE jl.CompanyId = @CompanyId
                      AND vr.SupersededByRevisionId IS NULL
                      AND vr.VoucherDate <= @AsOfDate
                    GROUP BY jl.LedgerId
                ),
                Accumulated AS (
                    SELECT jl.LedgerId AS Lid,
                           SUM(jl.AmountMinor) AS NetMinor
                    FROM JournalLines jl
                    JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    WHERE jl.CompanyId = @CompanyId
                      AND vr.SupersededByRevisionId IS NULL
                      AND vr.VoucherDate <= @AsOfDate
                    GROUP BY jl.LedgerId
                ),
                -- Profit is income less expense. Income ledgers carry a credit (negative) signed
                -- sum and expense ledgers a debit (positive) one, so BOTH negate to contribute
                -- positively to profit: profit = -SUM(net) across the two natures.
                NetProfit AS (
                    SELECT COALESCE(SUM(-a.NetMinor), 0) AS ProfitMinor
                    FROM Accumulated a
                    JOIN Ledgers l ON l.Id = a.Lid
                    JOIN AccountGroups g ON g.Id = l.AccountGroupId
                    WHERE g.Nature IN ('Income', 'Expense')
                )
                -- Amounts leave here in the raw signed convention — debit positive, credit
                -- negative — exactly as JournalLines holds them. Turning a liability's credit
                -- balance into the positive figure a reader expects is presentation, and it is
                -- done once, in the report service's mapper. This query used to flip the sign too,
                -- and the two flips cancelled: every liability line on the balance sheet came out
                -- with the wrong sign and the sheet only balanced when both sides were zero.
                SELECT l.Id AS LedgerId,
                       l.Name AS LedgerName,
                       g.Name AS AccountGroupName,
                       g.Nature AS Nature,
                       COALESCE(p.BalanceMinor, 0) AS AmountMinor
                FROM Ledgers l
                JOIN AccountGroups g ON g.Id = l.AccountGroupId
                LEFT JOIN Position p ON p.Lid = l.Id
                WHERE l.CompanyId = @CompanyId
                  AND g.Nature IN ('Asset', 'Liability')
                  -- The Profit & Loss A/c ledger is excluded here and injected below instead.
                  -- Its figure is derived (§8.4), which is why it is the one ledger barred from
                  -- manual posting; including it listed it twice, at 0.00 and at its real value.
                  AND l.AllowsManualPosting = 1

                UNION ALL

                SELECT NULL,
                       'Profit & Loss A/c',
                       'Capital Account',
                       CASE WHEN np.ProfitMinor >= 0 THEN 'Liability' ELSE 'Asset' END,
                       -- Signed like every row above: a profit is a credit on the liabilities
                       -- side, a loss a debit on the assets side. One expression covers both.
                       -np.ProfitMinor
                FROM NetProfit np
                ORDER BY 3, 2;
                """,

            // Every current revision in range, cancelled ones included and flagged (§8.4): a
            // cancelled revision carries no lines, so it arrives once with a null ledger and amount.
            [AccountingQueryKeys.DayBookForPeriod] = """
                SELECT vr.VoucherDate AS VoucherDate,
                       vr.Number AS VoucherNumber,
                       vt.Name AS VoucherTypeName,
                       vt.Kind AS Kind,
                       l.Name AS LedgerName,
                       jl.AmountMinor AS AmountMinor,
                       jl.Narration AS Narration,
                       v.Id AS VoucherId,
                       vr.Status AS Status
                FROM Vouchers v
                JOIN VoucherRevisions vr ON vr.VoucherId = v.Id
                    AND vr.SupersededByRevisionId IS NULL
                JOIN VoucherTypes vt ON vt.Id = v.VoucherTypeId
                LEFT JOIN JournalLines jl ON jl.VoucherRevisionId = vr.Id
                LEFT JOIN Ledgers l ON l.Id = jl.LedgerId
                WHERE v.CompanyId = @CompanyId
                  AND vr.VoucherDate >= @FromDate
                  AND vr.VoucherDate <= @ToDate
                ORDER BY vr.VoucherDate, vr.NumberNormalised, jl.LineNumber;
                """,

            // The drill-down target of every report: one row per posting line against a ledger.
            [AccountingQueryKeys.LedgerVouchersForPeriod] = """
                SELECT v.Id AS VoucherId,
                       vr.VoucherDate AS VoucherDate,
                       vr.Number AS VoucherNumber,
                       vt.Name AS VoucherTypeName,
                       vt.Kind AS Kind,
                       jl.AmountMinor AS AmountMinor,
                       jl.Narration AS Narration
                FROM JournalLines jl
                JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    AND vr.SupersededByRevisionId IS NULL
                JOIN Vouchers v ON v.Id = vr.VoucherId
                JOIN VoucherTypes vt ON vt.Id = v.VoucherTypeId
                WHERE jl.CompanyId = @CompanyId
                  AND jl.LedgerId = @LedgerId
                  AND vr.VoucherDate >= @FromDate
                  AND vr.VoucherDate <= @ToDate
                ORDER BY vr.VoucherDate, vr.NumberNormalised, jl.LineNumber;
                """,

            // One voucher type's register, same shape as the ledger statement.
            [AccountingQueryKeys.VoucherRegisterByType] = """
                SELECT v.Id AS VoucherId,
                       vr.VoucherDate AS VoucherDate,
                       vr.Number AS VoucherNumber,
                       vt.Name AS VoucherTypeName,
                       vt.Kind AS Kind,
                       jl.AmountMinor AS AmountMinor,
                       jl.Narration AS Narration
                FROM JournalLines jl
                JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    AND vr.SupersededByRevisionId IS NULL
                JOIN Vouchers v ON v.Id = vr.VoucherId
                JOIN VoucherTypes vt ON vt.Id = v.VoucherTypeId
                WHERE jl.CompanyId = @CompanyId
                  AND v.VoucherTypeId = @VoucherTypeId
                  AND vr.VoucherDate >= @FromDate
                  AND vr.VoucherDate <= @ToDate
                ORDER BY vr.VoucherDate, vr.NumberNormalised, jl.LineNumber;
                """,

            // The numbers a series has actually issued this financial year (§8.7); the service
            // recovers each sequence position and reports the missing ones. Parsing stays out of
            // SQL because prefix/suffix handling is business logic, not a provider difference.
            [AccountingQueryKeys.VoucherNumberGaps] = """
                SELECT vr.Number AS Number
                FROM VoucherRevisions vr
                WHERE vr.CompanyId = @CompanyId
                  AND vr.SeriesId = @SeriesId
                  AND vr.FinancialYearId = @FinancialYearId
                  AND vr.SupersededByRevisionId IS NULL
                ORDER BY vr.Number;
                """,

            // The edit log, newest first, optionally narrowed to a period and/or one entity. Read-
            // only by contract: the table's triggers reject any UPDATE or DELETE regardless of who
            // issues them (§11.4). OccurredAtUtc comparisons are text-safe: both EF Core and the
            // ADO parameter binding write DateTimeOffset in the same canonical form.
            [AccountingQueryKeys.EditLogForPeriod] = """
                SELECT Sequence,
                       OccurredAtUtc,
                       UserName,
                       MachineName,
                       Action,
                       EntityType,
                       EntityId,
                       ChangedProperties,
                       BeforeJson,
                       AfterJson
                FROM AuditLogEntries
                WHERE CompanyId = @CompanyId
                  AND (@FromDate IS NULL OR OccurredAtUtc >= @FromDate)
                  AND (@ToDate IS NULL OR OccurredAtUtc <= @ToDate)
                  AND (@EntityType IS NULL OR EntityType = @EntityType)
                  AND (@EntityId IS NULL OR EntityId = @EntityId)
                ORDER BY Sequence DESC
                LIMIT @MaxRows;
                """,

            // The chain itself, oldest first, for the service to re-hash field by field (§11.5).
            [AccountingQueryKeys.EditLogChainVerification] = """
                SELECT Sequence,
                       PreviousHash,
                       RowHash,
                       OccurredAtUtc,
                       UserName,
                       MachineName,
                       SaveBatchId,
                       Action,
                       EntityType,
                       EntityId,
                       AggregateType,
                       AggregateId,
                       ChangedProperties,
                       BeforeJson,
                       AfterJson
                FROM AuditLogEntries
                WHERE Sequence >= @FromSequence
                ORDER BY Sequence;
                """,

            // Bank-ledger lines with their cleared marks (§12). Lines whose voucher was altered keep
            // appearing through their new revision with the mark absent — re-confirmation against
            // the bank is the intended behaviour, not a defect.
            [AccountingQueryKeys.BankReconciliationStatement] = """
                SELECT jl.Id AS JournalLineId,
                       vr.VoucherDate AS VoucherDate,
                       vr.Number AS VoucherNumber,
                       vt.Name AS VoucherTypeName,
                       jl.AmountMinor AS AmountMinor,
                       jl.Narration AS Narration,
                       brm.BankDate AS BankDate
                FROM JournalLines jl
                JOIN VoucherRevisions vr ON vr.Id = jl.VoucherRevisionId
                    AND vr.SupersededByRevisionId IS NULL
                JOIN Vouchers v ON v.Id = vr.VoucherId
                JOIN VoucherTypes vt ON vt.Id = v.VoucherTypeId
                LEFT JOIN BankReconciliationMarks brm ON brm.JournalLineId = jl.Id
                WHERE jl.CompanyId = @CompanyId
                  AND jl.LedgerId = @LedgerId
                  AND vr.VoucherDate >= @FromDate
                  AND vr.VoucherDate <= @ToDate
                ORDER BY vr.VoucherDate, vr.NumberNormalised, jl.LineNumber;
                """,
        };
}
