# Phase 5 — GST Returns & Tax Invoice Compliance

```yaml
Phase: 5
Title: GST Returns & Tax Invoice Compliance
Status: Draft
Source: project.md §4.5, §10 (Phase 5)
Depends on: Phases 1–4
Blocks: Phase 6
Agent: compliance-agent (frontier tier — do not downgrade)
```

> **Draft.** Do not implement until `Status: Approved`.

## 1. Deliverables (project.md §10)

GST computation (CGST/SGST/IGST/cess), HSN/SAC masters, GSTR-1/3B/9 + JSON export,
GSTR-2A/2B reconciliation.

## 2. Scope notes

- **Tax invoice format per Rule 46** — all mandatory particulars present and printed.
- **Computation:** intra-state → CGST + SGST; inter-state → IGST; plus cess where applicable.
  Place-of-supply drives the split, not the billing address alone.
- **HSN/SAC masters**, per-item assignment, rate history with effective dates. **Nothing hardcoded** —
  rates and codes change by notification and must be a data update.
- **Reverse charge** and **composition scheme** handling.
- **GSTR-1** including the **Table 12 B2B/B2C HSN split**, **GSTR-3B**, **GSTR-9**, and
  **GSTR-2A/2B reconciliation** against purchase records.
- **JSON export in the current GSTN schema.** The schema version in use is recorded with each export
  so a schema change is a visible, dated event rather than a silent break.
- Rounding rules applied per invoice per GST convention, and the rounding difference posted to a
  designated ledger — never silently dropped.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C5.1 | A generated GSTR-1 JSON validates against the current GSTN schema for a test dataset |

Additional:

| # | Criterion |
|---|---|
| C5.2 | Intra-state and inter-state invoices produce the correct CGST/SGST vs IGST split for a fixture set |
| C5.3 | GSTR-3B summary figures reconcile to the same period's GSTR-1 and the books |
| C5.4 | GSTR-2B reconciliation correctly classifies matched / missing-in-books / missing-in-portal lines |
| C5.5 | A rate change with an effective date applies to invoices after that date and not before |

## 4. Open questions to close before approval

- Company GSTIN(s), state, and whether more than one GSTIN bills from this install — `open-questions.md` #8.
- Is the business under composition scheme for any registration? Any reverse-charge purchases in practice?
- Are exports / SEZ / e-commerce supplies in scope, or purely domestic B2B/B2C?
- Which GSTN schema version to target at implementation time — record it here before approval.
