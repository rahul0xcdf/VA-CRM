# Feature — Stock Valuation & Reports

**Status: implemented and verified, Phase 2.**

## What & why

FIFO and weighted-average costing (C2.4), on-hand and valuation reporting, the C2.1 reconciliation
of stock value to the "Stock-in-Hand" control account, reorder alerts, stock aging buckets, and the
entry-screen item lookup over a large catalogue (C2.6).

## User workflow

Inventory → **Stock reports**: pick a report, an as-of date, press **Run report**.

- *On hand* — quantity and value per item per godown.
- *Valuation vs control account* — total stock value beside the control ledger's balance; the two
  figures must agree (C2.1). A mismatch raises a warning dialog.
- *Reorder alerts* — items at or below their reorder level.
- *Stock aging* — what is still on the shelf, bucketed ≤30 / ≤90 / ≤180 / >180 days since receipt.

## Developer notes

- `StockValuationService` replays one item's ledger rows in `GlobalSequence` order up to the
  business date. FIFO consumes oldest layers first; weighted average carries a running
  (quantity, value) position. A shortfall prices at the last known cost and warns — never dilutes
  the average across units that were not there (`open-questions.md` #20).
- Effective method = item override ?? company default (`InventorySettings`, FIFO at launch).
- Per-unit costs round once, banker's rounding, at the single point a cost is born
  (`StockLedgerEntry.UnitCostFromValue`); values are always `ComputeValueMinor(quantity, cost)` —
  exact integer arithmetic end to end.
- Reports run through the Dapper seam: every statement lives in `InventorySqliteStatements`,
  aggregates integer minor quantities/values only (SQLite sums TEXT decimals in floating point —
  forbidden by ADR-0003), converts back to decimals once in `StockReportService`. The C2.6 lookup
  resolves entirely inside SQLite against the Items indexes; verified over a 5,000-SKU fixture.
- Aging replays receipts/issues strictly in posting order, so an issue can only consume stock that
  existed before it.

## Verification

- C2.4 `ValueIssue_FifoAcrossTwoLayers_CostsOldestFirst`,
  `ValueIssue_FifoAfterFirstIssue_ContinuesFromTheRemainingLayer`,
  `ValueIssue_WeightedAverage_CostsAtRunningAverage` (hand-computed fixtures);
  full-stack `Post_ReceiptsThenIssue_FifoValuationMatchesHandComputation`,
  `Post_IssueUnderWeightedAverage_CostsAtRunningAverage`.
- C2.1 `ReconcileValuation_AfterReceiptIssueTransfer_MatchesControlAccount`.
- C2.6 `SearchItems_OverFiveThousandSkus_ReturnsCorrectRowsWithinBudget`.
