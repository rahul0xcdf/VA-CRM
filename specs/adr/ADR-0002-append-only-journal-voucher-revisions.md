# ADR-0002 — The append-only journal: voucher revisions instead of in-place alteration

- **Status:** Proposed (accepted on approval of `specs/phase-1-accounting-core.md`)
- **Date:** 2026-08-14
- **Phase:** 1
- **Relates to:** `project.md` §4.1 (immutable double-entry journal, edit log), §10 (Phase 1);
  `specs/phase-1-accounting-core.md` §8; ADR-0001

## Context

`project.md` §4.1 requires an "immutable double-entry journal" *and* an edit log recording every
alteration with before/after values, which cannot be disabled by any user — mirroring TallyPrime's
Edit Log and MCA Rule 3(1), in force 1 April 2023.

Those two requirements together rule out the obvious implementation. A user must be able to correct a
voucher — accountants correct vouchers constantly, and a product that forces cancel-and-re-enter for
every typo will not be used. So "immutable" cannot mean "unalterable"; it has to mean "the record of
what was posted is never destroyed."

The decision is *how* alteration is represented in the schema, because every report written from
Phase 1 to Phase 10 will carry that choice in its `WHERE` clause, and changing it later means
rewriting every one of them.

## Options considered

**A. Mutate the voucher and its lines in place; write before/after images to the edit log.**
Simplest, and what most small accounting apps do. Rejected: the journal is then not immutable, and
the edit log becomes the *only* record of the prior state. A single defect in the logging path — or
one corrupted log row — loses history irrecoverably. It also contradicts §4.1 as written.

**B. Reversal-and-repost.** An alteration posts a contra entry reversing the original, then a fresh
voucher with the new values. Rejected for Phase 1: it triples the row count in the day book, shows the
user three vouchers where they made one correction, and needs its own numbering treatment for the
reversal. It is the right shape for a *statutory amendment* of a document already filed with a
regulator, which is why Phase 5 may still need it for GST amendments — but it is the wrong default
for "the operator typed 4500 instead of 5400."

**C. Revision chain (chosen).** The voucher is a stable identity; its content lives in versioned
revisions; only the newest revision is "current"; journal lines belong to a revision and are never
touched again.

**D. Full event sourcing.** Store commands, project the ledger. Rejected: correct but unfamiliar,
and every report becomes a projection. `project.md` prefers boring; this is accounting software where
a reviewer must be able to read the schema and believe it.

## Decision

1. **`Voucher` is a permanently immutable identity row** — `Id`, `CompanyId`, `VoucherTypeId`,
   `SeriesId`, `FinancialYearId`. Nothing about it ever changes. This GUID is the stable id Phase 8
   exports to Tally, and Tally's "overwrite if GUID exists" behaviour is what makes that stability
   load-bearing (ADR-0001 §5).

2. **`VoucherRevision` holds all content** — status, business date (`DateOnly`), voucher number,
   narration, references, change reason, plus `RevisionNumber` and `CreatedAtUtc` / `CreatedBy`.

3. **`JournalLine` rows belong to a revision and are insert-only.** No posted line is ever updated or
   deleted, by any code path.

4. **Alteration writes a new revision with a new complete set of lines**, and sets
   `SupersededByRevisionId` on the previous revision. That column is the *single* mutable field in the
   whole voucher subtree: it transitions once from `NULL` to a value and never changes again, enforced
   by a domain guard that throws on a second attempt, and asserted by checkpoint C1.9.

5. **"Current" is the predicate `SupersededByRevisionId IS NULL`**, not a boolean flag — the same
   column that answers "is this current?" also answers "what replaced it?", which the edit-log viewer
   needs. Supported by a partial index (SQLite) / filtered index (SQL Server), so both providers give
   it an index seek.

6. **Deletion does not exist.** Cancelling writes a new revision with `Status = Cancelled` and **zero
   journal lines**. Balances then exclude it with no special case in any report, the voucher number
   stays occupied (which is what makes the gap-blocking numbering rule in the Phase 1 spec §7.2
   actually hold), and the prior revision's lines stay readable forever.

7. **The voucher number lives on the revision**, with a filtered unique index over
   `(CompanyId, VoucherTypeId, SeriesId, FinancialYearId, NumberNormalised)` restricted to current
   revisions. A renumber is therefore just another alteration, and both the old and the new number
   remain permanently visible.

8. **This is independent of, and not a substitute for, the edit log.** The revision chain preserves
   *what the books said*; the edit log (Phase 1 spec §11) records *who changed it, when, and from
   what*, for masters as well as vouchers. Neither is derivable from the other, and the Rule 3(1)
   obligation is met by the log, not by this ADR.

## Consequences

**Positive.** The journal is genuinely immutable, so a corrupted or incomplete edit log costs
attribution but not history. Reports need no "was this deleted?" logic — a cancelled voucher simply
has no lines. Tally round-tripping gets a stable voucher GUID whose content can legitimately change
between exports. The "before" side of an edit-log entry can be cross-checked against a real, still-
present revision, which makes the log verifiable rather than merely assertable.

**Negative.** Storage grows with alterations: each one duplicates the full line set. At this
business's volume that is negligible, and the alternative is discarding evidence to save disk. There
is also one more join in every reporting query, and a second concept ("revision") the UI must not
leak — the voucher screen shows a voucher, with history behind a "View history" action.

**Watch for — the real hazard.** *A report that forgets the `SupersededByRevisionId IS NULL` filter
will silently double-count every altered voucher.* It will not error, it will not look obviously
wrong on a small dataset, and it will be found by an accountant months later. Mitigations, all of
which are Phase 1 obligations:

- every accounting `SqlQueryKey` reads from a single shared base predicate defined once per dialect,
  not from a hand-written `FROM JournalLine` in each statement;
- a test posts a voucher, alters it, and asserts that *every* key in `AccountingQueryKeys` returns the
  same totals before and after the alteration (an unfiltered query fails this immediately);
- any new accounting query added in a later phase must extend that test, and reviewers should treat a
  new query without it as a defect.

**Revisit if.** Phase 5 finds that GST amendments require reversal postings for statutory reasons
(option B) — that would be an *addition* alongside revisions, for documents already filed, and needs
its own ADR rather than a change to this one.
