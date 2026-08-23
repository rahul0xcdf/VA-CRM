# Customer pricing engine

**Phase 4 · Customer & Pricing Engine** (`specs/phase-4-customer-pricing.md` §2, checkpoints
C4.1/C4.2/C4.4/C4.5)

## Feature

Selecting a customer on a quotation, sales order or invoice **auto-fills the correct category
price** on every staged line (C4.1). Three pricing bases are supported, chosen per item-category
line (and per customer special price) by a basis flag (§4a, `open-questions.md` #3):

| Basis | Meaning | Rate computed as |
|---|---|---|
| `FixedPrice` | an explicit selling price | the stored amount |
| `MarginOnCost` | markup on the item's current cost | cost × (1 + margin%) |
| `MarginOnSalePrice` | discount off the item's default sale price | MRP × (1 − percent%) |

**Resolution order** for any sale line: **customer special price → category price-list line →
item master default sale price → nothing**. A margin tier whose basis value is missing or zero
declines and falls through rather than silently selling at zero.

**Conversion never reprices** (§4a): quotation → order → challan → invoice copies line prices
forward unchanged; the price list is consulted only when a line is created or a customer is
picked on an unsaved draft.

**Override, permission-gated** (C4.2/C4.5): the operator may type over an auto-applied rate;
the line then carries `AppliedRateMinor` (what the engine applied), the moved-off `RateMinor`,
and `IsPriceOverridden` — so the edit log shows the original and the override even when the
override happened before the first save. The gate itself is the `Sales.AllowPriceOverride`
setting (Masters → Sales settings): when off, the rate cell locks in the UI **and** the
document service refuses any sale line whose rate moved off its applied price — no UI path
bypasses it.

**Credit terms** (§2): saving a sales order or tax invoice evaluates outstanding + document
value against the customer's limit. Policy `Warn` asks for confirmation; `Block` refuses.
Quotations and challans are checked never — they promise nothing.

## User workflow (Documents screen)

1. Stage a new quotation/order/invoice, pick the **party** — a ledger that belongs to a
   customer. Status reports *"Prices applied for …"*; each line's **Priced from** column shows
   the tier that won (customer / category / item default).
2. Add more lines — each prices itself as it lands.
3. To override, edit the rate cell: the tier label turns amber. Reverting to the applied price
   clears the override.
4. Save. Under Block policy a crossed limit stops the save; under Warn a dialog asks.
5. Turn the gate off (Sales settings) and the rate cells lock on every sale screen.

## Developer notes

- `IPricingService.ResolveForSaleAsync` (`Application/Customers/PricingService.cs`) is the only
  pricing brain; margin math is decimal-computed once and rounded once through `MoneyScale`
  (ADR-0003 idiom).
- Evidence travels on `TradeDocumentLine` (`AppliedRateMinor`, `IsPriceOverridden`, `PriceSource`
  — enum by name) and through `TradeDocumentLineInput`/`TradeDocumentLineSummary`, so an
  edit-and-save cycle never launders an override into a manual rate.
- Service backstop: `TradeDocumentService.ValidateHeaderAndLinesAsync` refuses overridden sale
  lines when `SalesSettings.AllowPriceOverride` is false.
- The item master gained one nullable `DefaultSalePriceMinor` column — the MRP basis and the
  final fallback (recorded in spec §4a).
- Until Phase 9 brings user roles, the settings row IS the override permission store (spec §2).

## Verification (C4.1–C4.5)

- **C4.1**: real-DB test — a customer in a category with a price line resolves that price.
- **C4.2**: override-before-save persists evidence and the audit after-image contains
  `AppliedRateMinor` / `IsPriceOverridden` / `PriceSource`.
- **C4.4**: hand-computed fixtures for all three bases — cost 250 +20% → 300; MRP 500 −10% →
  450; fixed 175 — plus precedence tests (special beats category beats item default).
- **C4.5**: UI locks the cell when the gate is off; service test proves a slipped override is
  rejected and an all-applied document saves.
