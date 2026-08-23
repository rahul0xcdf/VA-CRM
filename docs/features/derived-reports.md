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

## Verification

- `Post_BalancedPayment_UpdatesEveryDerivedStatement` asserts TB/day book/P&L/BS line-by-line with
  hand-computed figures including the loss-on-assets presentation.
- Trial balance totals exactly zero over the whole workflow suite (implicit every run).
