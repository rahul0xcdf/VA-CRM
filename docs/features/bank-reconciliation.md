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

- `ListBankLedgersAsync` derives "is a bank ledger" from the group's position in the chart — at or
  under Bank Accounts / Bank OD A/c — and matches those group names **through
  `NameNormaliser.Normalise`**. It previously compared the display casing ordinally against the
  upper-cased `NameNormalised` column, so it always returned an empty list and this screen's
  account picker could never populate on any install (`phase-1-evidence.md` §6.3). Cash is
  deliberately not a bank: it is not reconciled against a statement.

## Verification

- `PostingWorkflowTests.ListBankLedgers_ForALedgerUnderBankAccounts_ReturnsIt` — a ledger created
  under Bank Accounts is returned, and Cash is not. This is the only direct test of the picker;
  until it existed the screen was unreachable and nothing failed.
- Otherwise covered indirectly by the posting suite (statement query exercised via service tests
  through the same SQL path) and manual smoke: marked/unmarked lines against a posted receipt.
