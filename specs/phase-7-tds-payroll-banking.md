# Phase 7 — TDS/TCS, Payroll Basics, Banking

```yaml
Phase: 7
Title: TDS/TCS, Payroll Basics, Banking
Status: Draft
Source: project.md §4.5, §10 (Phase 7)
Depends on: Phase 1 (and Phase 5 for tax interaction)
Agent: compliance-agent (frontier tier — do not downgrade)
```

> **Draft.** Do not implement until `Status: Approved`.

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
  Deep payroll stays out of v1 (`project.md` §5).
- **Banking:** cheque printing against configurable bank layouts; e-payment / NEFT / RTGS / UPI
  reference fields on payment vouchers.

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

## 4. Open questions to close before approval

- Which TDS/TCS sections actually apply to this business today?
- Is TCS on sale of goods (206C(1H)) in play given turnover?
- How many employees — does "payroll statutory basics" mean PF/ESI/PT returns, or just salary vouchers?
- Bank(s) and cheque layouts to support.
