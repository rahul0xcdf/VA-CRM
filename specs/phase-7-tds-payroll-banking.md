# Phase 7 — TDS/TCS, Payroll Basics, Banking

```yaml
Phase: 7
Title: TDS/TCS, Payroll Basics, Banking
Status: Approved
Source: project.md §4.5, §10 (Phase 7)
Depends on: Phase 1 (and Phase 5 for tax interaction)
Agent: compliance-agent (frontier tier — do not downgrade)
```

> **Approved 18 Sep 2026** by the project owner, with the §4 questions closed into §4a below.
> Delivered in two batches per §4a D5: **7a** TDS/TCS and banking, then **7b** payroll.
>
> On §5: the owner confirmed the payroll headcount is not known and **is expected to grow**. That is
> the case D3 was designed for — applicability stays a flag and every statutory threshold stays a data
> row, so crossing the PF (20) or ESI (10) employee limits is a settings change and never a migration.

## 1. Deliverables (project.md §10)

Configurable TDS/TCS section/rate/threshold masters, basic payroll statutory output,
cheque printing, e-payment/UPI reference fields.

## 2. Scope notes

- **TDS/TCS as data, never code.** Sections, rates, thresholds, payee types and effective-date ranges
  live in master tables. The **1 April 2026** Income Tax Act 2025 renumbering (old 194-series sections
  → new Sections 392/393/394 with numeric payment codes) must be absorbable as a **data update**, not
  a rewrite. That means: no section number appears as a constant, an enum, or a `switch` arm anywhere
  in `src/`, and both the old and new numbering can be active simultaneously across the cutover date.
- Threshold logic is cumulative per payee per financial year, not per transaction.
- Lower/nil deduction certificates (Sec 197) as a per-payee override with validity dates.
- **Payroll: statutory basics only** — enough for compliance output, not a payroll product.
  Deep payroll stays out of v1 (`project.md` §5). Scope fixed in §4a D3: employee master, pay heads,
  salary voucher, and PF/ESI/PT as effective-dated config behind per-statute flags. Form 24Q and
  Form 16 are **not** in Phase 7.
- **Statutory rates are data on the same terms as tax rates.** Non-negotiable #6 names HSN/SAC, GST,
  TDS and TCS; §4a D3 extends the identical rule to PF and ESI contribution rates, their wage
  ceilings, and Professional Tax slabs. A statutory threshold that changes by notification must never
  be a constant in `src/`.
- **Banking:** cheque printing against configurable bank layouts; e-payment / NEFT / RTGS / UPI
  reference fields on payment vouchers. Layouts reuse the Phase 3 print-template engine rather than a
  second template system (§4a D4); no bank layout is seeded.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C7.1 | A TDS-applicable purchase voucher correctly computes and posts the deduction using the config table, not a hardcoded rate |

Additional:

| # | Criterion |
|---|---|
| C7.2 | Adding a new section as a data row — with no code change and no rebuild — makes it usable |
| C7.3 | Cumulative threshold: the deduction starts at the transaction that crosses the annual limit, on the correct base |
| C7.4 | A dated rate change applies correctly to vouchers on either side of the effective date |
| C7.5 | A grep of `src/` finds no hardcoded section number or rate |

| C7.6 | 194Q and 206C(1H) are mutually exclusive on one transaction: where the buyer deducts 194Q, the seller's TCS must not also apply |
| C7.7 | With every payroll statutory flag off, no PF/ESI/PT row is produced and no statutory code path runs |
| C7.8 | The owner repositions a cheque layout and prints to a real cheque without a rebuild |

## 4. Open questions to close before approval

*(Closed — see §4a.)*

- ~~Which TDS/TCS sections actually apply to this business today?~~
- ~~Is TCS on sale of goods (206C(1H)) in play given turnover?~~
- ~~How many employees — does "payroll statutory basics" mean PF/ESI/PT returns, or just salary vouchers?~~
- ~~Bank(s) and cheque layouts to support.~~

## 4a. Decisions (18 Sep 2026)

### D1 — TDS sections at launch: 194C only

The owner names **194C (contractors and transport)** as the section actually deducted under today.
Nothing else is seeded.

This costs nothing to get wrong, and that is the point of C7.2: a section is a **data row**, so adding
194J or 194I later is an afternoon of data entry, not a code change. Two worth checking with the
business's CA before the first quarter closes, because they catch most trading businesses:

- **194J** — professional and technical fees. Any business paying an auditor is usually deducting here.
- **194I** — rent, if the godown or premises are rented (10% land/building, 2% plant and machinery).

Recording them here so the omission is a decision on the record rather than an oversight.

### D2 — 194Q and 206C(1H): both built, both gated by a config flag

Applicability is a **data flag on the company, never inferred from computed turnover**. This follows
the Phase 6 precedent for e-invoicing applicability (§4a of `phase-6-einvoice-eway.md`), and for the
same reason: turnover as the software computes it is not necessarily turnover as the statute defines
it, and a compliance obligation that switches itself on silently is worse than one that is declared.

With both flags off, neither runs — so if the business is below ₹10 crore today, this costs a boolean
and nothing else, and crossing the threshold later is a settings change rather than a migration on
tables that by then hold production rows.

**The mutual exclusion is enforced, not documented** (C7.6). Where the buyer deducts 194Q on a
purchase, the seller does not also collect 206C(1H) on the same transaction. Getting this wrong
double-taxes a line, and it is the single most common error in TCS implementations.

### D3 — Payroll: salary vouchers and statutory deductions as data; returns and certificates deferred

The owner did not have a headcount to hand and asked for the call to be made here. It is made as
follows, and the reasoning matters because headcount is exactly what would otherwise decide it:

**In scope for Phase 7:**

- An **employee master** — name, PAN, UAN, ESI number, joining and leaving dates, salary structure.
- **Pay heads as configuration** — earnings and deductions, each with a computation basis, the same
  way a price-list line carries its basis in Phase 4.
- A **salary voucher** posting through the journal engine, exactly as the Phase 2 stock voucher does.
- **PF, ESI and Professional Tax as effective-dated statutory config**, each switched on by its own
  flag, with contribution rates, wage ceilings and PT slabs as data rows.
- **Challan-ready output** — a report the owner can pay from.

**Explicitly out of scope for Phase 7:**

- **Form 24Q** (salary TDS return) and **Form 16** (employee certificate). These are *returns*, they
  need the TDS engine's return machinery finished first, and they are a phase's work on their own.
  Deferred to Phase 8 or later, deliberately and on the record.

**Why this line and not another.** PF is mandatory above 20 employees and ESI above 10, and both
numbers are statutory thresholds that change. Building a PF engine for a business not covered by PF
is waste; *not* building one for a business that is covered is a compliance failure. Treating
applicability as a flag and the thresholds as data dissolves the question — the same trick D2 uses,
and the one non-negotiable #6 already mandates for tax. With the flags off the module is inert
(C7.7), so the decision survives whatever the headcount turns out to be.

This reading of "basic statutory output" (`project.md` §10) is the literal one: statutory output is
what the law requires you to *pay and file*. A PF challan is that; a Form 16 is a certificate
downstream of it.

### D4 — Cheque printing: no seeded bank layouts, one configurable layout as versioned data

The owner did not name a bank. **No answer is needed**, because seeding bank layouts would be wrong
either way.

A cheque layout is field positions on a page. That is precisely what the Phase 3 print-template
engine already stores as versioned data and renders through the built-in FlowDocument renderer
(ADR-0004). Phase 7 reuses it rather than inventing a second template system: one adjustable layout,
edited by the owner against a real cheque, saved as a new version like any other template.

This is also defensible on the compliance side. **CTS-2010** standardised Indian cheque dimensions
and field zones, so a single adjustable layout covers most banks with position tweaks rather than a
per-bank rewrite. Where a bank differs, it is another template row — the same shape as the two seeded
tax-invoice templates that already resolve by priority (C3.6).

### D5 — Delivered in two batches, TDS first

Phase 7 as a whole is large, and only half of it carries a deadline. It ships as:

- **7a — TDS/TCS and banking.** The section/rate/threshold engine, Section 197 certificates, deduction
  posting, cheque layouts, and the NEFT/RTGS/UPI reference fields.
- **7b — payroll.** Everything in D3.

7a first because the **1 April 2026** renumbering is a fixed external date and the schema that absorbs
it must be right before production rows accumulate against it. 7b carries no equivalent deadline.

This keeps the "one phase per batch" rule in `CLAUDE.md` intact — it is one phase, sequenced — and
lets the payroll slice be sized against a real headcount once the owner has one, without holding up
the part that cannot slip.

## 5. One fact still wanted (does not block approval)

Roughly how many people are on the payroll? It does not gate 7a at all, and it does not gate 7b's
design — D3 is built to be correct either way. It only decides whether the PF and ESI flags are
switched **on** at go-live, which is a settings change. Worth confirming before 7b is walked through.

## 4b. Changes made beyond the approved spec (21 Sep 2026)

Recorded here as the working agreement requires: schema or public-contract changes stay minimal and
additive within the phase's scope, and the reason goes on the record. All three below are additive
and none needed a data migration.

### `PrintTemplate.DocumentKind` retyped to `PrintDocumentKind`

D4 rules that cheque layouts reuse the Phase 3 print-template engine rather than standing up a
second template system. That engine typed its kind as `TradeDocumentKind` — an enum whose own
documentation reads "the seven documents of the sales and purchase pipelines". A cheque is a bank
instrument, not a trade document; widening that enum would have put one inside it and grown a dead
arm on every pipeline `switch`.

`PrintDocumentKind` carries the seven trade members under identical names and numbers, plus
`Cheque`. The column stores enum **names**, so every existing template row reads back unchanged and
this change produces an empty migration diff. `TradeDocumentPrinting.ToPrintKind()` is the seam
between the two enums, written as an explicit switch rather than a cast so they are free to diverge.

### `PrintTemplateLayout.Fields` added

Cheque layouts need absolute field positions in millimetres; invoices lay themselves out and do not.
Added as a nullable trailing parameter with a `PlacedFields` projection normalising null to empty.
Nullable because template rows written before Phase 7 carry no such property in their stored JSON —
normalising on read rather than migrating the JSON keeps every existing revision readable exactly as
it was saved, which is the point of versioning them.

### `TaxPayeeProfile.DeclaresDeductingOnPurchases` added

Not in §2's field list, but checkpoint C7.6 cannot hold without it. The statute makes 194Q and
206C(1H) mutually exclusive on one transaction, but the seller cannot see the buyer's books: the
only thing that can switch off collection is the buyer's **written declaration**, so the declaration
is what is recorded. Inferring it from anything else would be inventing a fact about a third party's
tax position. Default false, which collects — the safe default of the two, because over-collecting
is refundable through the buyer's return while under-collecting is the seller's liability with
interest.

## 6. Defects found outside this phase's scope

Two pre-existing Phase 5 defects surfaced during implementation and were fixed rather than left in
place. Both are described in full in `specs/evidence/phase-7-evidence.md` §5.

1. **`GstRateService.AddAsync` rejected every rate change.** Its overlap guard tested
   `Covers(effectiveFrom)` against the open-ended row it was meant to supersede; an open-ended row
   covers every later date by definition, so the guard always fired and the close-the-previous-row
   branch behind it was unreachable. No Phase 5 test added a second rate row for one code. It
   surfaced because this phase's TDS rate service was modelled on the same method and inherited the
   same defect, which C7.4 caught at once. Both are fixed; `GstRateScheduleTests` is the regression.
2. **`EnsureGstSeedAsync` was never called from startup.** An install that had not opened a GST
   screen had no output/input tax ledgers for a tax line to post to. Now called from `App.OnStartup`
   and the first-run wizard alongside the new TDS and payroll seeds.
