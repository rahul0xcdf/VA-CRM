# Feature — Voucher Entry & Journal Engine

**Status: implemented and verified, Phase 1.**

## What & why

The journal engine is the product's core: everything else eventually becomes balanced journal
lines. This feature provides the single parameterised voucher-entry screen (all eight voucher
types) over `IVoucherPostingService`, which enforces spec §8's posting rules and §8.2's append-only
alteration.

## User workflow

### Posting
1. Open **Vouchers → Voucher entry**. Press **Load lists** (voucher types + postable ledgers).
2. Pick a type, date, narration; fill at least two lines: one debit, one credit.
3. The running **Difference** must read exactly **0.00** before *Post voucher* enables.
4. On success the allocated number appears (e.g. `PMT/0001`) and the id is copied into the alter box.

### Alteration / cancellation / history
5. Paste a voucher id (auto-filled after posting), press **Load for alter** — the current revision
   loads into the same grid.
6. Edit lines/date, enter a mandatory **Reason**, press **Apply alteration** → a new revision is
   written; the old one stays readable forever.
7. **Cancel voucher** writes an empty cancelled revision: figures leave every statement, the number
   stays occupied.
8. **View history** shows that voucher's edit-log rows oldest-first.

Refusals (unbalanced by even ₹0.0001, locked period, inactive ledger, non-cash Contra target)
surface verbatim in the status line.

## Developer notes

- Engine: `VoucherPostingService` — validates first (§8.1), then per ADR-0002 Amendment 1:
  transaction → save supersession alone → save successor + lines → commit.
- Numbering: `VoucherNumberAllocator` claims numbers inside the caller's transaction after
  validation; manual overrides raise the high-water mark and are recorded (`IsManualNumber`).
- Financial years: `FinancialYearProvider` creates years lazily on first post (D1: April start).
- `JournalLine.VoucherId` is denormalised so edit-log rows file under their voucher without a join
  (`AddJournalLineVoucherId` migration); both revision and line implement `IBelongsToAggregate`.

## Verification

- `PostingWorkflowTests` (17 tests): balanced post updates TB/day-book/P&L/BS; one-minor-unit
  imbalance rejected with no number consumed; append-only alteration asserted byte-level; cancel
  keeps number; FY counter restarts; manual override advances high-water and blocks duplicates;
  period lock refuses postings and date moves into lock; Contra restricted **and a valid
  cash-to-bank Contra accepted**; chain intact.
- The Contra pair matters as a pair. `Post_ContraToNonCashLedger_IsRefused` was for a long time the
  only Contra test, and it passed while the rule rejected *every* Contra voucher: the seeded group
  names were matched case-sensitively against the upper-cased `NameNormalised` column, so nothing
  was ever "cash or bank" (`phase-1-evidence.md` §6.2). A rule that refuses everything also refuses
  what it should — a negative test alone cannot tell the two apart.
  `Post_ContraBetweenCashAndBank_IsAccepted` is the other half.
- Manual smoke: posted payment `PMT/0001`, altered it, cancelled it; day book flagged correctly.
