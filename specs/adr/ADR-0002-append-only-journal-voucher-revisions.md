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

## Amendment 1 — 15 Aug 2026: how supersession is written

**Status:** accepted by `architect-agent`; amends §4, §5 and §7 above without changing them.
**Raised by:** the Phase 1 implementing agent, who found that `SupersededByRevisionId` cannot carry a
foreign key. **Applies to:** the journal engine, before it is written.

### The conflict

Decision §7 puts a *filtered unique* index on the current revision's number. Decision §4 makes an
alteration insert a successor carrying the same number and stamp the predecessor. Those two produce
contradictory statement orderings the moment `SupersededByRevisionId` is also a foreign key:

- the foreign key makes the predecessor's `UPDATE` depend on the successor's row, so the `INSERT`
  must come first — at which instant both rows have `SupersededByRevisionId IS NULL` and the same
  number, and the filtered unique index rejects the insert;
- the opposite order points the `UPDATE` at a row that does not exist yet, and fails the foreign key.

Neither provider can rescue this. SQL Server has no deferred constraint checking at all, and no
provider defers *unique index* checking, so a constraint that is violated between two statements of
one transaction is violated, full stop. **This is not an EF Core artefact and it is not fixed by
splitting `SaveChangesAsync`** — with the foreign key present, no ordering of any number of saves
works.

### Decision

1. **`SupersededByRevisionId` carries no foreign key.** It is the last of the three exceptions Phase 1
   spec §5 grants to "every reference is a declared foreign key", alongside `AuditLogEntry.CompanyId`
   and `VoucherType.DefaultSeriesId`. The constraint that has to survive is the one the books depend
   on — at most one current revision per voucher number — and between the two, the unique index wins.

2. **Removing the foreign key is necessary but not sufficient, and the ordering must not be left to
   EF Core.** Without an FK edge, EF has no declared dependency between the two commands; and while
   EF does add ordering edges for unique-index *value* handoffs, it has no model of a *filtered*
   index's predicate, so it cannot know that the predecessor's update is what frees the number. The
   order it currently emits is a property of its command comparer, not a guarantee. Relying on it
   would leave a `UNIQUE constraint failed` that appears intermittently, in production, on the one
   operation an auditor cares most about.

3. **Supersession is therefore written in its own `SaveChangesAsync`, before the successor exists,
   inside one `IUnitOfWork` transaction.** A save boundary is a hard ordering guarantee; it is the
   only one available here. The procedure is specified verbatim in Phase 1 spec §8.2.

4. **The transaction is mandatory, not advisory.** Between the two saves the voucher has *no* current
   revision: its number is free and it is invisible to every report in §8.4. Committing that state
   would lose a voucher from the books, which is worse than the failure it replaces. The alteration
   therefore lives behind **one method on the posting service** that owns the transaction, rather than
   as a sequence any caller could assemble in the wrong order.

5. **What the lost foreign key was protecting is replaced by three tests**, not by a comment: the
   model-shape guard, the statement-order guard and the one-current-revision invariant (Phase 1 spec
   §14, C1.17–C1.19).

### Consequences

**Positive.** ADR-0002's central operation becomes writable, deterministically, on both providers, in
an order that is stated in the spec rather than inferred from a library's internals. The filtered
unique index survives intact, so the numbering rule keeps its database-level enforcement — which
Phase 9 needs, because under concurrency that index stops being a formality and becomes the last line
of defence.

**Negative, and accepted.** The revision chain has no declared referential integrity; a stale
`SupersededByRevisionId` is now possible in principle, catchable only by C1.19. And an alteration
spans two saves, so it produces two `SaveBatchId` values and reads as two rows in the edit log rather
than one. That is mitigated, not removed: `VoucherRevision` and `JournalLine` **must** implement
`IBelongsToAggregate` returning `("Voucher", VoucherId)`, which §11.1 requires anyway, so the
edit-log viewer groups an alteration by the voucher it belongs to. Widening `SaveBatchId` to mean
"one transaction" instead of "one save" was considered and rejected for now: it would make the
identifier depend on ambient state, and grouping by aggregate answers the auditor's question already.

**Watch for.** Anyone "fixing" the missing foreign key. C1.17 exists to fail loudly when they do, and
the reason must stay in the comment at
`src/VinayakaAgencies.Infrastructure/Persistence/Configurations/VoucherRevisionConfiguration.cs`.

**Revisit if.** Phase 9's concurrency work, or a defect, shows the two-save ordering discipline is not
holding. The documented fallback is to move number uniqueness off the revision entirely, onto a
`VoucherNumberAllocation` row keyed `(CompanyId, VoucherTypeId, SeriesId, FinancialYearId,
NumberNormalised)` and pointing at the `Voucher`. An ordinary alteration then touches no unique key at
all, the foreign key can be restored, and one save suffices. It was not chosen now because it
duplicates the voucher number into a second table — drift on the field that identifies a document to
a tax officer — to buy an ordering property that a save boundary already buys for free.
