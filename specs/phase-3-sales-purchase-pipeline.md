# Phase 3 — Sales & Purchase Pipeline + Template Designer

```yaml
Phase: 3
Title: Sales & Purchase Pipeline + Template Designer
Status: Draft
Source: project.md §4.3, §10 (Phase 3)
Depends on: Phases 1, 2
Blocks: Phases 4, 5, 6
```

> **Draft.** Do not implement until `Status: Approved`.

## 1. Deliverables (project.md §10)

Quotation → Sales Order → Delivery → Tax Invoice chain (and its Purchase mirror), FastReport
integration with at least two editable templates (quotation, tax invoice), atomic posting to
accounts + inventory.

## 2. Scope notes

- **Sales chain**, mirroring Tally's own pattern:
  1. **Quotation/Estimate** — no effect on stock or ledgers, freely re-editable, own numbering series.
  2. **Sales Order** — one-click from a quotation or from scratch; tracks pending qty/value;
     "Sales Order Outstanding" report.
  3. **Delivery/Challan** — optional stage for staged deliveries against one order.
  4. **Tax Invoice** — from an order or directly; posts to accounts **and** inventory atomically.
- **Purchase mirror:** Purchase Order → Goods Receipt → Purchase Invoice.
- **Atomicity:** invoice posting writes journal lines and stock ledger lines inside one
  `IUnitOfWork` transaction. A mid-way failure leaves neither. This is what the Phase 0 UoW seam exists for.
- **Conversion carries data forward without re-keying** — quotation lines, prices, discounts, terms.
  Converted-from links are stored both ways for drill-down.
- **FastReport .NET WPF**, with the **runtime designer** exposed to end users: logo, layout, visible
  fields and terms & conditions are user-editable, no rebuild and no developer. More than one
  template per document type (e.g. per branch or per customer category), with a default selection rule.
- Templates live as data (file + DB pointer), are versioned, and are included in the Phase 10 backup.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C3.1 | A quotation converts to an order to an invoice without re-keying data |
| C3.2 | A non-developer can open the template designer and change the logo or a field without a rebuild |
| C3.3 | Invoice posting is atomic — a failure midway leaves no partial stock or ledger update |

Additional:

| # | Criterion |
|---|---|
| C3.4 | Partial delivery against an order leaves the correct pending quantity on the outstanding report |
| C3.5 | A quotation has zero effect on the trial balance and zero effect on the stock ledger |
| C3.6 | Two templates exist for the tax invoice and the correct one is selected by rule |

## 4. Open questions to close before approval

- Are delivery challans actually used day to day, or is invoice-direct the norm?
- Are quotations numbered in one series or per branch/salesperson?
- Print stationery: pre-printed letterhead or full-page print? Paper size (A4/A5)? Copies (Original / Duplicate / Triplicate)?
