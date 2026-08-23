# Phase 4 Evidence Report — Customer & Pricing Engine

```yaml
Phase: 4
Verified by: development agent (ox-alpha session), owner review pending
Verified on: 2026-08-23
Repo state verified: branch phase4, Phase 4 batch committed through the session
```

This report lists artefacts produced by commands run in the implementation session against the
branch as committed. Screenshots are `NOT CAPTURED` until the owner walkthrough — they need the
app running interactively, which is the sign-off step itself.

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

| Project | Passed | Failed | Total |
|---|---|---|---|
| VinayakaAgencies.Domain.Tests | 328 | 0 | 328 |
| VinayakaAgencies.Application.Tests | 46 | 0 | 46 |
| VinayakaAgencies.App.Tests | 8 | 0 | 8 |
| VinayakaAgencies.Infrastructure.Tests | 157 | 0 | 157 |
| **Total** | **539** | **0** | **539** |

Per-project transcript (from the same session's `dotnet test` runs):

```
Passed!  - Failed:     0, Passed:   328, Skipped:     0, Total:   328 - VinayakaAgencies.Domain.Tests.dll
Passed!  - Failed:     0, Passed:    46, Skipped:     0, Total:    46 - VinayakaAgencies.Application.Tests.dll
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8 - VinayakaAgencies.App.Tests.dll
Passed!  - Failed:     0, Passed:   157, Skipped:     0, Total:   157 - VinayakaAgencies.Infrastructure.Tests.dll
```

Phase 4 additions to those suites: 21 domain tests (`tests/VinayakaAgencies.Domain.Tests/Customers/`),
11 application tests (`PricingServiceTests`, `SalesSettingsServiceTests` — three-basis fixtures,
precedence), 7 infrastructure workflow tests (`CustomerWorkflowTests`) and 1 navigation assertion.

## 3. Checkpoint criteria

| # | Criterion | Verdict | Artefact |
|---|---|---|---|
| C4.1 | Selecting a customer auto-fills the category price | VERIFIED (service + UI) | `CustomerWorkflowTests.ResolveForSale_CustomerInSeededCategory_ResolvesTheCategoryPrice`; UI: party pick reprices staged lines (`TradeDocumentsViewModel.OnPartyLedgerChanged`), new lines price on add |
| C4.2 | An override is possible but logged | VERIFIED | `CustomerWorkflowTests.OverrideBeforeSave_PersistsAppliedRateAndEvidence_WithAnEditLogRow` — line keeps `AppliedRateMinor ≠ RateMinor`, `IsPriceOverridden=true`; `AuditLogEntry.AfterJson` contains all three evidence fields |
| C4.3 | Statement matches invoices minus receipts | VERIFIED | `ICustomerStatementService.CustomerStatementAsync` asserted inside `BillwiseOutstanding_SixHundredReceipt_GoesToTheOldestBillFirst` — closing balance ₹800 = Σ invoices ₹1,400 − receipt ₹600 |
| C4.4 | All three pricing bases verified against hand-computed fixtures | VERIFIED | `PricingServiceTests`: fixed 175.0000; margin-on-cost 250 +20% → 300.0000; margin-on-sale MRP 500 −10% → 450.0000; precedence special → category → item default |
| C4.5 | Without override permission no UI path changes an auto-applied price | VERIFIED | UI: rate cell disabled when `Sales.AllowPriceOverride` off and a price applied (`TradeDocumentsView` template column + `InverseBooleanConverter`); service backstop `TradeDocumentService.ValidateHeaderAndLinesAsync` refuses overridden sale lines when the gate is off (workflow test in `Customers/`) |
| C4.6 | Part-payment applies against the oldest open bill | VERIFIED | `CustomerWorkflowTests.BillwiseOutstanding_SixHundredReceipt_GoesToTheOldestBillFirst`: older ₹1,000 + newer ₹400 invoices, one ₹600 receipt → older opens ₹400 / newer opens ₹400; credit policy pair in `CreditCheck_BlockPolicyRejectsOverLimit_WarnAllows` |

## 4. Decisions recorded during implementation

1. **Override evidence is structural, not a manual audit write** — the audit seam deliberately
   has no writer, so overrides are proved by data on the line itself (`AppliedRateMinor`,
   `IsPriceOverridden`, `SalePriceSource`), captured automatically by the SaveChanges audit
   interceptor. Migration `20260823110413_AddTradeLinePricing` backfills legacy rows as Manual.
2. **Per-customer special prices added at approval** (spec §4a) — resolution order customer
   special price → category price-list line → item default sale price; item master gained one
   nullable default-sale-price column.
3. **Credit check runs at draft save** for sales orders and tax invoices only; quotations and
   challans promise nothing. Days-overdue analytics deferred (recorded in spec §4a notes).
4. **Statement CSV export** ships now; Excel export lands with Phase 8 interop (ClosedXML),
   avoiding a package addition this phase.

## 5. Owner walkthrough checklist (screenshots pending)

- [ ] Masters → Customer categories: rename/extend the seeded four
- [ ] Masters → Category price lists: one FixedPrice line, one MarginOnCost, one MarginOnSalePrice per category
- [ ] Masters → Customers: create with ledger auto-created; add a special price
- [ ] Documents: pick customer → prices fill; override one rate → amber label (C4.1/C4.2)
- [ ] Sales settings: turn override off → rate cell locks; attempt save of changed rate refused (C4.5)
- [ ] Credit Block policy refusing an order beyond limit; Warn confirming (§2)
- [ ] Reports → Customer statement: load, print preview, CSV export; open bills oldest-first (C4.3/C4.6)
