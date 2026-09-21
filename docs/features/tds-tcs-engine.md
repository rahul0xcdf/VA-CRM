# TDS / TCS engine

**Phase 7a · TDS/TCS, Payroll, Banking** (`specs/phase-7-tds-payroll-banking.md` §2, §4a D1–D2)

## Feature

Tax deducted and collected at source, computed from **configuration rows and nothing else**. A
section is a data row the owner types; its rate, its two thresholds and its no-PAN uplift are dated
rows attached to it. Adding a section is two saves and it is usable on the next voucher — no code
change, no rebuild, no migration (C7.2). That is also how the **1 April 2026 renumbering** lands:
deactivate the old codes, insert the new ones, and run both side by side across the cutover.

What the engine gets right, because each is a common implementation defect:

- **Two thresholds, not one.** Deduction starts at whichever comes first — a single payment over
  the per-payment limit, or the year's aggregate crossing the annual one. Modelling only the annual
  figure under-deducts on a lone large payment; only the per-payment figure under-deducts on a
  stream of small ones.
- **Catch-up on the crossing payment.** The payment that carries the year past the annual limit is
  taxed on *everything not yet taxed this year*, not just on itself (C7.3). The next payment is
  taxed on itself alone — the catch-up happens once.
- **Dated rates.** A rate change applies from its own first day and to nothing earlier (C7.4).
  History is closed, never rewritten, so a filed return stays reproducible.
- **Section 197 certificates.** Per payee *and* per section, with validity dates. A nil certificate
  records a deduction of zero — which is a different fact from no deduction, and the return needs
  it. A capped certificate splits the payment across both rates rather than over- or under-claiming.
- **206AA.** No PAN on file means the higher rate, where the section has one — and "no PAN" is read
  from the payee profile, not guessed.
- **194Q vs 206C(1H) are mutually exclusive** (C7.6). Where the buyer declares in writing that they
  deduct on their purchase, we do not also collect on the same sale. Both are gated by per-company
  flags the owner declares; neither is inferred from computed turnover.

## User workflow

1. **TDS & Banking → TDS/TCS sections** — type the code and name, then attach a rate row with its
   payee class, rate, thresholds and effective-from date. Leave a threshold blank for "no such
   test"; leave both blank to deduct from the first rupee.
2. **TDS & Banking → TDS/TCS setup** — name the two liability ledgers, and declare whether the
   business deducts on purchases of goods or collects on sales of goods (both off by default).
   Record each party's payee profile here: class of person, PAN, and whether they have declared
   that *they* deduct on their purchases from us.
3. Post a deducting voucher through `ITaxDeductionService.PostAsync` — the expense side is entered,
   and the party is credited net while the deduction is credited to the payable ledger. The figures
   are computed, never re-keyed.
4. **TDS & Banking → TDS/TCS register** — the period's deductions at the rate that was applied.
   `Ctrl+Enter` opens the challan roll-up: what to deposit, per section, with the deductee count.

## Developer notes

- Domain: `TaxSection`, `TaxRateSchedule`, `TaxPayeeProfile`, `LowerDeductionCertificate`,
  `TaxDeduction`, `TdsSettings`, and the pure arithmetic in `TdsMath` (`Domain/Tds/`).
- Application: `ITaxSectionService` (masters), `ITaxDeductionService` (compute and post),
  `ITdsSettingsService` (the flags) — `Application/Tds/`.
- `TaxDeduction` is a **snapshot**: rate, thresholds and certificate number are copied on at
  posting time, never resolved through the master at read time. It is also the cumulative ledger —
  `CumulativeBaseAfterMinor` is stored, because a running total recomputed on every posting would
  move once a voucher is cancelled.
- The cumulative read is indexed on
  `(CompanyId, LedgerId, TaxSectionId, FinancialYearId, DeductionDate)` — C7.3's arithmetic is
  wrong if that read is wrong, and it happens on every deducting voucher.
- Posting composes `IVoucherPostingService`. A deduction reaches the books as journal lines through
  the Phase 1 engine or it does not reach them at all.
- Migration `AddTdsAndBanking`. `EnsureTdsSeedAsync` provisions the two payable ledgers and the
  settings row idempotently and seeds **no section, rate or threshold** — not even 194C, the one
  section §4a D1 names as in use today.

## Verification

- `TdsMathTests` — 17 hand-computed fixtures covering both threshold tests, the catch-up, a whole
  year taxed exactly once, the no-PAN rate, all four certificate cases, rupee rounding and overflow.
- `TdsWorkflowTests` — the vertical slice against a real SQLite file: C7.1 (computed from config and
  posted as balanced journal lines), C7.2 (a section added as data alone, and old/new numbering
  coexisting), C7.3, C7.4, C7.6, plus the certificate and challan paths.
- `NoHardcodedTaxFigureGuardTests` — **C7.5 as an executing test**, not a grep somebody remembers:
  no section number in any string literal or identifier anywhere in `src/`, and `TdsMath` holds no
  figure beyond the minor-unit scale.
