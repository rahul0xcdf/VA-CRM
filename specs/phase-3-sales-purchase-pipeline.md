# Phase 3 — Sales & Purchase Pipeline + Template Designer

```yaml
Phase: 3
Title: Sales & Purchase Pipeline + Template Designer
Status: Approved
Source: project.md §4.3, §10 (Phase 3)
Depends on: Phases 1, 2
Blocks: Phases 4, 5, 6
```

> **Approved 22 Aug 2026** by direct owner instruction ("implement the phase three"), with the
> §4 open questions closed as recorded in §4a below. The owner may amend any default; amendments
> are additive to the data model (statuses and templates are rows/enum values, not schema breaks).

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

Closed on approval (§4a). Recorded here verbatim for the audit of the decision:

- Are delivery challans actually used day to day, or is invoice-direct the norm?
- Are quotations numbered in one series or per branch/salesperson?
- Print stationery: pre-printed letterhead or full-page print? Paper size (A4/A5)? Copies (Original / Duplicate / Triplicate)?

## 4a. Decisions recorded at approval

| Question | Decision |
|---|---|
| Delivery challans vs invoice-direct | Invoice-direct is the norm; the challan remains available as a staging stage that carries godown and quantity forward into the invoice it converts to. Challans are **data-only**: they move no stock and post nothing, so the stock ledger and the control account stay reconciled by construction. Stock moves exactly twice in each direction — once when a tax invoice is posted, once when a purchase invoice is posted — which keeps checkpoint C3.3's atomicity surface minimal. |
| Quotation numbering | One series per company per document kind (`QTN/`, `SO/`, `DC/`, `INV/`, `PO/`, `GRN/`, `BILL/`), reusing the §7 numbering machinery as system voucher types. Branch/salesperson series is deferred to Phase 9 with multi-user/multi-branch. |
| Print stationery | A4 full-page print, no pre-printed letterhead assumed; logo, header lines and terms are template data. Copy labels (Original for Recipient / Duplicate for Transporter / Triplicate for Supplier) are configurable template fields per GST Rule 46. |

Implementation amendments (recorded under the working agreement):

1. **FastReport .NET WPF deferral** — FastReport .NET WPF requires a commercial licence before its
   runtime designer can ship. Phase 3 therefore lands document templates as versioned data
   (`PrintTemplate` + append-only `PrintTemplateRevision` rows) rendered through a built-in WPF
   `FlowDocument` preview/print window, with an editable designer screen that satisfies C3.2
   functionally today. ADR-0004 records the decision; swapping the renderer to FastReport later is
   a presentation-layer change because templates are data either way.
2. **Goods receipt is data-only** — the purchase mirror posts stock exactly once, at the purchase
   invoice, mirroring the sales side. A GRN records what physically arrived (against its order)
   so the outstanding report stays truthful without a clearing-account posting leg.

