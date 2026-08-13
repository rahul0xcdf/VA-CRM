# Phase 1 — Accounting Core

```yaml
Phase: 1
Title: Accounting Core
Status: Draft
Source: project.md §4.1, §10 (Phase 1)
Depends on: Phase 0
Blocks: Phases 2–10
```

> **Draft.** Expand this file, resolve the open questions it lists, and set `Status: Approved`
> before any implementation. The spec gate blocks `src/` edits until then.

## 1. Deliverables (project.md §10)

Masters (groups/ledgers), all core voucher types, journal engine, day book / trial balance /
balance sheet / P&L, edit-log / audit-trail.

## 2. Scope notes

- **Chart of accounts:** predefined Tally-style primary groups (Capital Account, Loans (Liability),
  Current Liabilities, Fixed Assets, Investments, Current Assets, Branch/Divisions, Misc. Expenses,
  Sales Accounts, Purchase Accounts, Direct/Indirect Income, Direct/Indirect Expenses, Suspense)
  plus user-defined groups and ledgers, nested to arbitrary depth.
- **Voucher types:** Payment, Receipt, Contra, Journal, Sales, Purchase, Credit Note, Debit Note.
  Each has its own numbering series (prefix / width / reset-on-FY / manual-override policy).
- **Journal engine:** every voucher posts a balanced set of journal lines. Posting is rejected unless
  `Σ debit == Σ credit` to the stored scale. The journal is **append-only** — an "alteration" writes
  a new version and an edit-log entry; it never mutates a posted line in place.
- **Derived balances:** ledger/group balances and all statements are computed from journal lines.
  No independently-writable balance column exists anywhere in the schema.
- **Cost centres / categories:** optional dimension on a journal line.
- **Multi-company:** every master and voucher carries `CompanyId` (the `Company` entity from Phase 0).
- **Financial year & period locking:** vouchers carry a business `DateOnly`; a closed period rejects
  new postings.
- **Bank reconciliation:** manual only in this phase (mark a line as cleared with a bank date).
- **Edit Log / Audit Trail — non-negotiable (`project.md` §4.1, MCA Rule 3(1), in force 1 Apr 2023):**
  every create / alter / delete of a voucher **or master** records user, UTC timestamp, entity type,
  entity id, action, and full before/after values. View-only in the UI. No setting, permission, or
  admin role can disable it, and there is no delete path for log rows. Implemented in
  `SaveChangesAsync` off the `IAuditStamped` seam built in Phase 0, so it cannot be bypassed by
  forgetting to call something.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C1.1 | Posting a balanced voucher updates trial balance, balance sheet and P&L correctly |
| C1.2 | An unbalanced voucher is rejected |
| C1.3 | Every create/alter/delete of a voucher or master appears in the audit trail with user + timestamp + before/after |
| C1.4 | The audit trail cannot be turned off — verified by a test that attempts to disable/delete log rows through every public path and fails |

Additional:

| # | Criterion |
|---|---|
| C1.5 | Trial balance totals to zero for a randomised set of ≥500 generated vouchers |
| C1.6 | Balance sheet balances: Assets == Liabilities + Equity + P&L for the same dataset |
| C1.7 | Day book for a date range matches the sum of its vouchers |

## 4. Open questions to close before approval

- Financial year start (assumed 1 April) — `open-questions.md` #9.
- Which predefined ledgers to seed beyond the primary groups (Cash, Bank, Profit & Loss A/c, …).
- Voucher numbering rules per type: reset annually? gaps allowed? manual override permitted?
- Does the owner want cost centres enabled at launch, or hidden until asked for?
