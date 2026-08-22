# Feature — Edit Log / Audit Trail & Tamper Evidence

**Status: implemented and verified, Phase 1.**

## What & why

MCA Rule 3(1): every create/alter/delete of every master and voucher is logged with user,
timestamp and full before/after values; it cannot be disabled by anyone (spec §11). The viewer is
strictly read-only — no delete, purge or hiding — and a SHA-256 hash chain makes tampering
*provable*.

## User workflow

1. Open **Audit → Edit log**, press **Load newest 200**.
2. To trace one row's history: type its entity type (e.g. `Ledger`, `VoucherRevision`) and id
   (GUID, "D" form), press **Filter**. From voucher entry, *View history* does this for you.
3. Press **Verify hash chain**: either "Chain intact over N rows" or the exact first broken
   sequence with why.

Reading a row: *Action* + *Changed properties* tell what happened; *Before/After JSON* are complete
row images, so any historical state is reconstructible from the log alone.

## Developer notes

- Written inside `AppDbContext.SaveChangesAsync` with required constructor dependencies (no
  interceptor that could be dropped); log rows commit in the same transaction as the change.
- Defences (§11.4): not an `Entity` (no repository path), tamper guard exception, database
  `RAISE(ABORT)` triggers, no API surface, no config keys.
- Hash chain: `AuditLogEntry.ComputeRowHash` seals each row over the previous hash; the same static
  re-verifies it in `AuditLogReader` — one hashing implementation for writers and verifiers.
- An alteration spans two save batches by design (ADR-0002 Amendment 1); grouping under one voucher
  works because revisions and journal lines carry `IBelongsToAggregate → ("Voucher", voucherId)`.

## Verification

- `EditLogChain_AfterFullWorkflow_VerifiesIntact` (>50 rows across provisioning + posting).
- `Alter_PostedVoucher_WritesAppendOnlyHistory` asserts supersession before/after images.
- Infrastructure guard tests already cover raw-context tampering rejection, trigger aborts, and
  canonical JSON encoding.
