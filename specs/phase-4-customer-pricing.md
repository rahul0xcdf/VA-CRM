# Phase 4 — Customer & Pricing Engine

```yaml
Phase: 4
Title: Customer & Pricing Engine
Status: Approved
Source: project.md §4.4, §10 (Phase 4)
Depends on: Phases 1, 2, 3
Blocks: Phase 5
```

> **Approved 23 Aug 2026** by direct owner instruction ("start to work on the phase 4"), with the
> §4 open questions closed as recorded in §4a below. The owner may amend any default; amendments
> are additive to the data model (categories and prices are master-data rows, not schema breaks).

## 1. Deliverables (project.md §10)

Customer master, customer categories, category-wise price list / margin table,
auto-apply-with-override, customer statement / outstanding.

## 2. Scope notes

- **Customer master:** contact, GSTIN, billing/shipping addresses, place of supply, credit limit,
  credit period, default price list.
- **Customer Category master** — each customer belongs to exactly **one** category. Categories are
  master data rows, not a code enum (`open-questions.md` #2).
- **Category-wise price list / margin table**, supporting **both** approaches per item-category pair
  (`open-questions.md` #3):
  - (a) a fixed selling price per item per category, and
  - (b) a margin/discount % computed over **cost** or over **MRP/sale price** — a per-line flag picks
    the basis, because different item lines need different treatment.
- **Auto-apply with permission-gated override:** selecting a customer on a quotation/order/invoice
  auto-fills the category price. A manual override requires the override permission and is written
  to the audit trail (Phase 1) with the original and overridden values.
- **Customer-wise billing:** per-customer invoice history, running ledger/statement, outstanding
  receivables (bill-wise), printable and exportable statement.
- **Per-customer special prices (added at approval):** an optional customer-level price line per
  item that wins over the category price when present. Resolution order for any sale line:
  customer special price → category price-list line → item master default sale price. The item
  master gains one nullable default sale price column — the base for margin-on-MRP lines and the
  final fallback; it is data, editable on the item screen.
- **Conversion never reprices:** converting quotation → order → challan → invoice copies line
  prices forward unchanged. The price list is consulted only when a line is created or manually
  refreshed.
- **Override gate until Phase 9:** user roles arrive in Phase 9; until then the override
  permission is a local, data-driven app setting (`Sales.AllowPriceOverride`, default on for the
  single-operator install). It is enforced in both the pricing service and the UI (the price cell
  is read-only when off), and every exercised override is written to the Phase 1 audit trail with
  original and overridden values.
- Credit limit / credit period produce a warning or a hard block on new orders — policy is a setting.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C4.1 | Selecting a customer on a new invoice auto-fills the correct category price |
| C4.2 | An override is possible but logged |
| C4.3 | A customer statement matches the sum of their invoices minus receipts |

Additional:

| # | Criterion |
|---|---|
| C4.4 | Both pricing bases (fixed price; margin on cost; margin on MRP) are each verified against a hand-computed fixture |
| C4.5 | A user without the override permission cannot change the auto-applied price by any UI path |
| C4.6 | Bill-wise outstanding correctly applies a part-payment against the oldest open bill |

## 4a. Decisions recorded at approval (23 Aug 2026)

| Question | Decision |
|---|---|
| Customer categories (`open-questions.md` #2) | Seed the placeholder four — **Retailer / Dealer / Wholesaler / Institutional** — as master data rows. Freely renamable and extendable; nothing hardcoded. |
| Pricing bases (`open-questions.md` #3) | **All three, chosen per item-category line:** fixed selling price; margin/discount % over cost; margin/discount % over MRP/sale price. A per-line basis flag selects which applies. |
| Conversion repricing | **Hold the quoted price.** Conversion copies line prices forward unchanged at every step of both chains; the price list is consulted only on line creation or explicit refresh. |
| Per-customer special prices | **Yes, in scope** — an optional customer-level price line per item that wins over the category price when present. Resolution: customer special price → category price list → item master default sale price. |
| Item master base price | The item master gains one nullable default sale price column (the MRP/sale basis for margin lines and the final fallback). Minimal, additive amendment to the simplified Phase 2 item master. |

Implementation amendments are expected to be appended here under the working agreement as they arise.
