# GST computation & tax invoice compliance

**Phase 5 · GST Returns & Tax Invoice Compliance** (`specs/phase-5-gst-returns.md` §2)

## Feature

Every posted invoice now computes and **stores its own tax snapshot**: per line the taxable
value, CGST/SGST or IGST, cess and the applied rate; on the document the place-of-supply state
code and reverse-charge flag. Intra-state supplies split tax as CGST+SGST (half each),
inter-state as IGST — decided by place of supply against the company's state, not the billing
address alone (C5.2). Invoice totals round to the rupee with the difference posted to a
designated round-off ledger (`Gst.RoundOffLedger` settings row) — never silently dropped.

Rates are **data, never code**: the HSN/SAC master carries rate schedules with effective dates,
so a notified change applies to invoices from that date and not before (C5.5). Items carry an
optional HSN/SAC assignment (picker on the Items screen); unconfigured items post at zero tax so
non-GST invoicing keeps working.

## User workflow

1. **GST → HSN/SAC codes** — enter codes once (code + description).
2. **GST → GST rate schedules** — add the rate (and cess) per code with its effective-from date;
   superseding a schedule closes it automatically.
3. **Masters → Items** — assign each item its HSN/SAC code.
4. Raise and post invoices exactly as in Phase 3 — the computation hooks into posting; no extra
   step. Reverse-charge purchases are flagged on the document (self-assessed output tax,
   Tally-style matching input leg).

## Developer notes

- Entities `HsnSac`, `GstRateSchedule`, `GstTaxMath` (`Domain/Gst/`); services
  `IHsnSacService`, `IGstRateService`, `IGstComputationService` (`Application/Gst/`).
- Snapshot columns are additive on `TradeDocument(Line)`; set only at posting time.
- Migration `AddGstCompliance`; seeding provisions tax ledgers + the round-off settings row
  idempotently (`EnsureGstSeedAsync`) but **no rates/codes** — those are owner data
  (non-negotiable #6).

## Verification

C5.2 intra/inter-state fixtures, C5.5 effective-date fixture and rounding tests in
`tests/VinayakaAgencies.Application.Tests/Gst/` + Domain Gst tests; suite green at implementation.
