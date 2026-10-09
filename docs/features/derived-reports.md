# Feature — Derived Reports (day book, trial balance, P&L, balance sheet)

**Status: implemented and verified, Phase 1.**

## What & why

Every reported number is derived from journal lines — no table stores a balance (spec §8.3, C1.12).
This feature exposes the four core statements as on-screen grids with drill-down.

## User workflow

- **Vouchers → Day book**: pick a range, *Load*. Cancelled vouchers appear flagged in red; totals
  shown underneath.
- **Reports → Trial balance**: pick "as on" date, *Load*. The sheet must read balanced to the paisa.
  Select any ledger row → its statement for the year-to-date loads in the pane below (drill-down).
- **Reports → Profit & loss**: pick a period, *Load*. Gross block left, indirect right, gross and
  net profit beneath.
- **Reports → Balance sheet**: pick a date, *Load*. Assets vs liabilities; the derived
  **Profit & Loss A/c** line carries the accumulated result so the sheet always balances. A loss
  appears on the asset side, Tally-style.

## Developer notes

- Reads go through `ISqlQueryExecutor` + named `AccountingQueryKeys` resolved by `SqliteDialect`
  (`AccountingSqliteStatements.cs`). No SQL string exists outside the dialect layer (C1.11).
- Every statement filters `SupersededByRevisionId IS NULL` and takes `@CompanyId` first.
- Aggregation is integer `SUM(AmountMinor)` (ADR-0003); conversion to rupees happens once, in the
  report service mappers — never floating point.
- Dates are stored/compared as canonical `yyyy-MM-dd` text; parameters are rendered through
  `CalendarText.For(...)` because Dapper cannot bind `DateOnly`.
- Balance-sheet profit is accumulated-since-inception (there are no year-end closing postings), so
  the identity holds across financial years (C1.6).
- **The liability sign is normalised in exactly one place: `AccountingReportService.MapBalanceSheetRow`.**
  `BalanceSheetAsOf` hands over raw signed minor units — debit positive, credit negative, as
  `JournalLines` holds them — and carries no presentation logic at all. Both sites used to flip the
  sign and the two cancelled, so every liability line showed inverted and the sheet only balanced
  when both sides were zero (`phase-1-evidence.md` §6.1). When the Phase 9 SQL Server dialect is
  written, it must keep this rule: no sign logic in SQL.
- The derived `Profit & Loss A/c` row is **injected** by the second half of that query's `UNION`
  and the real ledger is excluded from the first half by `AllowsManualPosting = 1`. Without the
  exclusion a company in profit saw the line twice, once at 0.00.

## Verification

- `Post_BalancedPayment_UpdatesEveryDerivedStatement` asserts TB/day book/P&L/BS line-by-line with
  hand-computed figures including the loss-on-assets presentation. Note that its single payment
  leaves **both balance-sheet sides at zero**, which is why it could not catch the sign defect.
- `BalanceSheet_WithABalanceOnEachSide_ShowsLiabilitiesPositiveAndBalances` is the test that can:
  one credit purchase, a creditor shown positive on the liabilities side, one P&L A/c row only.
- `RandomisedLedgerFixtureTests` asserts the trial balance totals to zero and the balance sheet
  balances over **523 randomised vouchers** across 15 ledgers of five natures (C1.5, C1.6). Set
  `VA_EVIDENCE_DIR` to have that run write the three statements out for an evidence file.
- Trial balance totals exactly zero over the whole workflow suite (implicit every run).
