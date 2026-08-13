# Phase 4 — Customer & Pricing Engine

```yaml
Phase: 4
Title: Customer & Pricing Engine
Status: Draft
Source: project.md §4.4, §10 (Phase 4)
Depends on: Phases 1, 2, 3
Blocks: Phase 5
```

> **Draft.** Do not implement until `Status: Approved`.

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

## 4. Open questions to close before approval

- Actual category names — `open-questions.md` #2.
- Margin basis — `open-questions.md` #3.
- Price-list effective dating: do old quotations reprice on conversion, or hold the quoted price?
- Are per-customer special prices needed on top of per-category, or is category enough?
