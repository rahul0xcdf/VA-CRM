# Phase 2 Evidence Report — Inventory

```yaml
Phase: 2
Verified by: development agent, owner review pending
Verified on: 2026-10-05
Repo state verified: branch phase-7-tds-payroll-banking, at c37e563 plus this session's commits
Spec: specs/phase-2-inventory.md (Approved 22 Aug 2026, amendments in §2a)
```

Phase 2 was implemented and verified by tests in August 2026; this file is the evidence report that
was never filed. It transcribes the artefacts rather than re-deriving them: the criteria are
asserted by tests that have been in the suite since the phase was built, and all of them pass in
the run recorded in §2.

**All six criteria are met.** The one caveat is C2.1's scope, noted in §1.

---

## 1. Checkpoint results

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| C2.1 | Stock valuation reconciles to the inventory GL control account | **Met** | `InventoryWorkflowTests.ReconcileValuation_AfterReceiptIssueTransfer_MatchesControlAccount` — receipt, issue and transfer posted, then the stock ledger's valued position compared against the control account's balance derived from the journal |
| C2.2 | Selling a serial-tracked item records which unit serial was sold | **Met** | `InventoryWorkflowTests.Post_SerialTrackedSale_RecordsExactlyWhichUnitWasSold`; domain lifecycle in `SerialNumberTests.MarkSold_InStockSerial_RecordsSaleAgainstLine` (the sale links back to the line, not merely a status flag) |
| C2.3 | The same serial cannot be sold twice; attempting it is rejected | **Met** | `InventoryWorkflowTests.Post_SecondSaleOfSameSerial_IsRejectedAndNothingMoves` — the rejection leaves no partial movement; `SerialNumberTests.MarkSold_AlreadySold_Throws`; and the receipt side, `Validate_ReceiptRepeatingAnExistingSerial_IsAnErrorNotAWarning` — a duplicate serial arriving is an error, not a warning, because a warning cannot conjure a second physical unit |
| C2.4 | FIFO and weighted-average each verified against a hand-computed fixture | **Met** | `InventoryWorkflowTests.Post_ReceiptsThenIssue_FifoValuationMatchesHandComputation` and `…Post_IssueUnderWeightedAverage_CostsAtRunningAverage` end to end; the arithmetic itself in `StockValuationServiceTests` — `ValueIssue_FifoAcrossTwoLayers_CostsOldestFirst`, `ValueIssue_FifoAfterFirstIssue_ContinuesFromTheRemainingLayer`, `ValueIssue_WeightedAverage_CostsAtRunningAverage`, plus the per-item override (`ValueIssue_ItemOverride_WinsOverCompanyDefault`) and the default (`EffectiveMethod_NoOverride_ReturnsCompanyDefault`) that open-questions.md #19 settles as FIFO |
| C2.5 | An inter-godown transfer changes location without changing total stock value | **Met** | `InventoryWorkflowTests.Transfer_ChangesLocationWithoutChangingTotalValue`; the balanced-legs invariant in `StockVoucherTests.ValidateTransferBalance_ItemShortByOneUnit_Throws` and `…ValidateTransferBalanced_BalancedAcrossGodowns_Passes` |
| C2.6 | Item search by name/code over a ≥5,000-SKU fixture, within the UI budget | **Met** | `InventoryWorkflowTests.SearchItems_OverFiveThousandSkus_ReturnsCorrectRowsWithinBudget` — 5,000 SKUs seeded, the 100 matching `PUMP-25` all returned and nothing else, in under the 2,000 ms ceiling (the query itself resolves in single-digit milliseconds on SQLite). **Amended from brand+HP to name/code on approval** — open-questions.md #5 dropped the motor/pump attribute columns, and §2a records the amendment |

### On C2.1's scope

The reconciliation asserted is between the valued stock ledger and the control account **after a
receipt, an issue and a transfer**. That is the criterion. What it does not cover is a company whose
books have run for years — the reconciliation over the Phase 1 ≥500-voucher fixture, or over a
multi-year dataset, is not asserted anywhere. Nothing suggests it would fail; it simply has not been
run at that volume, and the equivalent Phase 1 work (§3 of `phase-1-evidence.md`) is now the
precedent for doing it.

---

## 2. Build and test run

```
dotnet build VinayakaAgencies.sln -c Debug
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
dotnet test VinayakaAgencies.sln -c Debug
```

```
Passed!  - Failed: 0, Passed: 485, Skipped: 0, Total: 485, Duration:    523 ms - VinayakaAgencies.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 112, Skipped: 0, Total: 112, Duration:    205 ms - VinayakaAgencies.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 175, Skipped: 0, Total: 175, Duration:      5 s - VinayakaAgencies.App.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 278, Skipped: 0, Total: 278, Duration:     59 s - VinayakaAgencies.Infrastructure.Tests.dll (net8.0)
```

**1,050 tests, 0 failures.** The run covers the whole product through Phase 8 — the suites are not
partitioned by phase, so this is the state of Phase 2's tests as of today rather than as of the
August batch.

The Phase 2 tests inside it:

| Suite | Tests | Covers |
|---|---|---|
| `InventoryWorkflowTests` | 10 | C2.1–C2.6 end to end, plus negative stock and kits (§3) |
| `StockValuationServiceTests` | 5 | Both valuation methods, the per-item override, the default |
| `StockLedgerEntryTests` | 5 | Increase/decrease exclusivity, the zero-quantity refusal, value computation rounding half-to-even **once**, cost recovery within rounding |
| `StockVoucherTests` | 8 | Line-kind rules per purpose, counter-ledger rules, transfer balance, single-attachment |
| `SerialNumberTests` | 7 | The C2.2/C2.3 lifecycle: sell, re-sell refusal, return to stock, move godown without selling |
| `ItemTests` | 9 | The simplified master of §2a — name normalisation, unit/factor rules, serial-and-batch exclusivity, reorder level |

---

## 3. Approved amendments, as built

The spec was approved with four decisions recorded in §2a. Each is visible in the tests above:

1. **Simplified item master** (open-questions.md #5, resolved by the owner 22 Aug 2026). No
   brand / model / HP / phase / voltage / discharge / head / warranty columns. `ItemTests` asserts
   the master that was built: name (+ optional code), units, stock group, tracking flags, valuation
   override, reorder level. HSN/SAC moved to Phase 5 as data-driven config rows.
2. **FIFO default** (#19), with a per-item override selecting weighted average. Both verified
   against hand-computed fixtures — C2.4.
3. **Warn on negative stock** (#20): `Post_IssueBeyondOnHand_WarnsThenPostsOnlyWhenConfirmed` — the
   entry goes through only after an explicit confirmation, and the resulting negative stays visible
   in stock reports. Serial-tracked items remain hard-blocked, because a warning cannot produce a
   serial that was never received.
4. **C2.6 amended to name/code search**, following from #5.

Kits are covered by `Post_KitIssue_DepletesComponentsAtTheirOwnCosts` — a kit issue depletes each
component at that component's own cost, not at an average of the kit.

## 4. Quantities and values are scaled integers

ADR-0003 was extended to stock in this phase: quantity and value both persist as scaled integer
minor units, never `decimal` on a SQLite `TEXT` column, so a `SUM()` over them cannot become an
IEEE-754 double. `StockLedgerEntryTests.ComputeValueMinor_RoundsHalfToEvenOnce` is the one worth
reading — the rounding happens once, at a defined point, rather than accumulating per line.

The model guard tests that enforce this across the whole schema (no decimal-on-TEXT column, no
cascade deletes, enums stored by name) are in `AccountingModelTests` and
`UniqueIndexNullSemanticsTests` and hold for the Phase 2 tables as well as the Phase 1 ones.

---

## 5. Defect found and fixed after this phase

`StockValuationService.ReconcileValuationAsync` **threw on a company with no stock movements** — a
pre-existing Phase 2 defect found during Phase 8 and fixed there (recorded in
`phase-8-interoperability.md` §4b). It would have surfaced as the Stock Valuation screen crashing
on a new install, before any stock had been received. It is noted here because it belongs to this
phase's code, and because it is the second instance of the pattern Phase 1's evidence also found:
a path that only runs on an empty or first-use database, which no fixture-based test visits.

---

## 6. Owner walkthrough checklist (screenshots pending)

Screenshots are `NOT CAPTURED` — they need the app running interactively, which is the walkthrough
itself. The five Inventory screens:

- [ ] Masters → Stock groups: create, rename, re-parent (note: the selected-row load defect in this
      screen was fixed under ADR-0007 — confirm Save renames the selected group, not whatever the
      editor last held)
- [ ] Masters → Godowns: create; same selected-row check
- [ ] Masters → Units: create a base unit and an alternate with a conversion factor
- [ ] Masters → Items: create a simple item; create a serial-tracked one; set a reorder level;
      set a per-item valuation override (C2.4)
- [ ] Masters → Kits: define a kit and its components
- [ ] Stock vouchers: opening, receipt, issue, transfer. Confirm the transfer leaves total value
      unchanged (C2.5) and that an issue beyond on-hand warns before posting (§3.3)
- [ ] Stock reports: valuation reconciling to the control account (C2.1), reorder alerts, aging
      buckets
- [ ] Item lookup during entry over the real catalogue, for the feel of C2.6 rather than its
      millisecond ceiling
