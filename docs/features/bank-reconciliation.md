# Feature — Bank Reconciliation (manual)

**Status: implemented and verified, Phase 1.**

## What & why

Marks which posted bank-ledger lines have cleared the bank (spec §12). Marks live in their own
table because the journal is insert-only — reconciliation state is operational metadata that may be
added, changed and removed, each change edit-logged. Statement import/auto-matching is Phase 7.

## User workflow

1. Open **Reports → Bank reconciliation**, press **Load bank ledgers** (only ledgers under
   Bank Accounts / Bank OD A/c appear).
2. Choose ledger + date range, press **Statement**.
3. Select a line, set **Cleared on** to the bank's date, press **Mark cleared** — or select a marked
   line and **Withdraw mark** if it bounced.
4. If a voucher was altered afterwards, its new lines appear unreconciled on purpose: re-confirm
   against the bank statement.

## Developer notes

- `IBankReconciliationService` / `BankReconciliationService`: statement via the
  `BankReconciliation.Statement` query key; marks via ordinary repository writes (audit capture logs
  create/update/delete with full images). One mark per line (unique index).
- Known consequence, by design: marks pin a specific `JournalLine`; alteration creates new lines, so
  items reappear uncleared.

## Verification

- Covered indirectly by the posting suite (statement query exercised via service tests through the
  same SQL path) and manual smoke: marked/unmarked lines against a posted receipt.
