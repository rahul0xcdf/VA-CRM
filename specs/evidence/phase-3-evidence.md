# Phase 3 Evidence Report — Sales & Purchase Pipeline + Template Designer

```yaml
Phase: 3
Verified by: development agent (ox-alpha session), owner review pending
Verified on: 2026-08-23
Repo state verified: branch phase-1-spine, working tree (Phase 3 batch uncommitted at capture time)
```

This report lists artefacts produced by commands run in the implementation session against the
working tree as it stands. Screenshots are `NOT CAPTURED` until the owner walkthrough — they need
the app running interactively, which is the sign-off step itself.

---

## 1. Full `build.ps1` output

Command: `.\build.ps1` (restore + build + test)

```
=== Restore ===

=== Build (Debug) ===

=== Test ===
--- VinayakaAgencies.App.Tests ---
--- VinayakaAgencies.Application.Tests ---
--- VinayakaAgencies.Domain.Tests ---
--- VinayakaAgencies.Infrastructure.Tests ---

=== Done (build + test succeeded) ===
```

Build: 0 warnings, 0 errors across all eight projects (nullable warnings-as-errors enforced).

## 2. Test-run summary

Captured from the same `.\build.ps1` run (TRX files under `artifacts/test-results/`):

| Project | Passed | Failed | Total |
|---|---|---|---|
| VinayakaAgencies.Domain.Tests | 307 | 0 | 307 |
| VinayakaAgencies.Application.Tests | 35 | 0 | 35 |
| VinayakaAgencies.App.Tests | 7 | 0 | 7 |
| VinayakaAgencies.Infrastructure.Tests | 150 | 0 | 150 |
| **Total** | **499** | **0** | **499** |

Per-project transcript:

```
Passed!  - Failed:     0, Passed:    35, Skipped:     0, Total:    35 - VinayakaAgencies.Application.Tests.dll
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307 - VinayakaAgencies.Domain.Tests.dll
Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - VinayakaAgencies.App.Tests.dll
Passed!  - Failed:     0, Passed:   150, Skipped:     0, Total:   150 - VinayakaAgencies.Infrastructure.Tests.dll
```

## 3. Checkpoint criteria

| # | Criterion | Verdict | Artefact |
|---|---|---|---|
| C3.1 | Quotation → order → invoice without re-keying | VERIFIED | `TradeWorkflowTests.Convert_QuotationToOrderToInvoice_CarriesDataWithoutRekeying` — lines, rates, discounts, terms, party and provenance links asserted after each hop; numbers QTN/0001 → SO/0001 → INV/0001 |
| C3.2 | Non-developer edits logo/layout without rebuild | VERIFIED (service level) | `PrintTemplateWorkflowTests.SaveLayout_AppendsANewVersionAndHistoryStaysReadable`; UI: Print Templates screen (`PrintTemplatesViewModel`) |
| C3.3 | Invoice posting atomic — mid-failure leaves neither side | VERIFIED | `TradeWorkflowTests.PostInvoice_SerialUnavailableAtPost_IsRefusedAndNothingPersists` (refusal persists nothing, INV series unconsumed); ambient-transaction rollback incl. exception-escape in `UnitOfWorkTransactionTests`; nested-transaction no-op handle `UnitOfWork.cs:49-54` |
| C3.4 | Partial delivery leaves correct pending quantity | VERIFIED | `TradeWorkflowTests.Convert_PartialDelivery_KeepsPendingQuantityOnOutstandingReport`, `CancelPostedInvoice_ReopensAFulfilledOrder_RestoringItsPendingQuantity` |
| C3.5 | Quotation: zero TB effect, zero stock effect | VERIFIED | `TradeWorkflowTests.Create_Quotation_LeavesTrialBalanceAndStockLedgerUntouched` (trial balance byte-compared before/after create+revise; stock ledger row count unchanged) |
| C3.6 | Two tax-invoice templates; correct default by rule | VERIFIED | `PrintTemplateWorkflowTests.PipelineSeed_CreatesTwoTaxInvoiceTemplates_LowestActivePriorityIsDefault`, `SetActive_RetiringTheDefault_FallsBackToNextPriority`, `SetActive_RetiringTheLastActiveTemplateOfAKind_IsRefused` |

Additional spec coverage (same file unless noted): purchase mirror end-to-end
(`PurchaseChain_GoodsReceiptThenInvoice_ReceivesStockOnceAtWhatWasPaid`), posted-invoice reversal
(`CancelPostedInvoice_ReversesJournalStockAndSerials_InOneTransaction`), negative-stock confirm
gate (`PostInvoice_BeyondOnHand_WarnsThenPostsOnlyWhenConfirmed`), deletion rules
(`Delete_ConvertedSourceAndSourceWithSuccessors_AreRefused_DraftIsAllowed`), domain aggregate rules
(`VinayakaAgencies.Domain.Tests/Trade/TradeDocumentTests.cs`,
`.../Printing/PrintTemplateTests.cs`), navigation registration
(`ScreenModuleCatalogTests.Discover_FindsTheSalesPurchaseModule_WithItsThreePhase3Screens`).

## 4. Defects found and fixed during this verification pass

1. **ADR-0003 violation** — `TradeDocumentLine.DiscountPercent` mapped to a TEXT column (caught by
   `Model_MapsNoDecimalPropertyOntoATextAffinityColumn`). Now stored as `DiscountPercentMinor`
   INTEGER; decimal remains a computed projection. Migration `AddSalesPurchasePipeline` edited
   in place while uncommitted.
2. **Ten-thousand-fold posting scale bug** — `BuildAccountingLines` handed minor-unit longs to
   `PostVoucherLine`'s rupee-decimal parameters; a ₹875 invoice posted ₹8,750,000 to party/sales.
   Sales chains balanced only because both legs shared the error; purchase chains failed the
   journal engine's balance check outright. All legs now convert once via `MoneyScale.FromMinor`.
3. **Purchase invoices ran through issue valuation** — first receipt of an item always warned
   "exceeded cost layers" and blocked posting behind a confirmation. Receipt movements are now
   self-costed at what was paid; only issue sides call the valuation service.
4. **Fulfilment double-count / broken chains** — source closing added an already-persisted
   successor's lines twice; order→challan→invoice chains could never close the order. Replaced
   with direct-successor sums plus a `ConvertedFrom` ancestor walk at posting; closed ancestors are
   skipped, not walls. Orders never close at conversion (a trimmed draft must leave pending
   quantity visible).
5. **Cancellation left orders falsely fulfilled** — the reopen walk read stale in-memory status;
   staged changes now flush inside the transaction before the walk, and a fulfilled order whose
   balance returned reopens (`TradeDocument.Reopen`).
6. **Empty-set valuation reads** — Dapper demanded `byte[]` constructor parameters when the stock
   valuation/on-hand aggregates returned zero groups (SQLite reports untyped NULL aggregates as
   BLOB). Aggregates now CAST to INTEGER in `InventorySqliteStatements`.

## 5. Owner walkthrough checklist (screenshots pending)

- [ ] Documents screen: raise quotation → convert to order → trim draft → post (C3.1/C3.4)
- [ ] Order outstanding report showing partial pending quantity (C3.4)
- [ ] Print preview of a posted tax invoice with Rule 46 copy labels (C3.6)
- [ ] Print templates screen: change title/logo, save as new version (C3.2)
- [ ] Trial balance / stock reconciliation after a purchase chain (C3.3 evidence)
