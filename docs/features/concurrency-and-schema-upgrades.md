# Concurrency, voucher numbering and schema upgrades

Phase 9. Spec: `specs/phase-9-multiuser.md` §2, §4a D1/D3. Checkpoints C9.1, C9.3 and (unmet) C9.4.

## The feature

The three things that change when a second person starts billing: a save must not quietly erase
somebody else's, two counters must not be handed the same invoice number, and the database must stop
upgrading itself while three clients are launching.

## Lost updates

Two people open the same customer. Both save. Without a concurrency check the second write erases
the first, no error appears anywhere, and nobody finds out until the credit limit is wrong.

Every mutable entity now carries a `ConcurrencyToken` that `SaveChangesAsync` replaces on each
insert and update. EF matches on the value that was loaded, so a row somebody else has already
changed matches nothing, and the save is refused with `ConcurrentUpdateException` — whose message
names what to reload.

### Why not `rowversion`

It is the obvious choice and it is unavailable. SQLite has no equivalent, and C9.4 wants the
Phase 1–8 suites to pass *unchanged* on both providers. A token the domain maintains behaves
identically on each, so "unchanged" keeps meaning what it says.

### Why not on `Entity`

That would put a token on the append-only types — `VoucherRevision`, `JournalLine`,
`StockLedgerEntry` — where it is dead weight and, worse, implies those rows can be updated, which
ADR-0002 forbids. `IConcurrencyStamped` draws the line where the model already draws it.

### Why not a shadow property

It was one first, and the mistake is worth knowing because it looked right: no entity class had to be
edited and the tests passed. It broke every service that reads with `AsNoTracking` and then calls
`Update` on the detached instance — a no-tracking read carries no shadow values, so re-attaching
sent an all-zero original token, matched no row, and turned an ordinary save into a spurious
lost-update. `GstRateScheduleTests` caught it.

### Existing rows

The migration adds the column with an all-zero default, so every row in a Phase 1–8 database starts
on the same token. Detection still works from the first write: whoever saves first replaces it, and
the second client's `WHERE` finds nothing.

## Voucher numbering

`open-questions.md` #11 is Confirmed on both halves: numbers are never reused, **and** gaps are
blocked. Together they rule out the easy implementations — a `SEQUENCE` is duplicate-free and loses
numbers on rollback and on cache eviction at restart; a running maximum over the vouchers is gap-free
and hands two counters the same number.

Phase 1 already had the right shape: a counter row per (series, financial year), which is also how
the reset-each-year rule works without a job somebody has to remember to run in April. What was still
client-side was the read-modify-write around it.

The allocator now, in this order:

1. **Makes sure the counter row exists.** A lock cannot be taken on a row that is not there, and the
   first allocation of a financial year is exactly that case. Two counters racing *there* are
   separated by the unique `(SeriesId, FinancialYearId)` index instead — one post is refused rather
   than two numbers issued, and since allocation happens after validation the refused post consumes
   nothing.
2. **Locks it and reads the mark under the lock.**
3. **Advances its tracked copy to what the lock saw.** Waiting for a lock is precisely the window in
   which the other counter allocated, and re-reading through the repository would return the cached
   instance rather than the truth.

The lock is a no-op `UPDATE` that sets the token to itself. It is the one row lock that reads the
same on both providers: `WITH (UPDLOCK, HOLDLOCK)` is a syntax error on SQLite, and a plain read
takes only a shared lock, so two counters would both read the mark and then both try to promote.
Setting the column to itself rather than bumping it matters — a bump would make the caller's own save
look like somebody else's change and fail its own concurrency check, so every post would report a
conflict against nobody. There is a test for that specifically.

`AdvanceTo` never lowers the mark. A number that has been handed out is spent whatever happens next,
so moving the mark backwards is the one operation that could issue it twice.

## Schema upgrades

Auto-migrating at launch was fine while the product was single-user. With two or three clients they
all start in the morning, and several of them can be inside `Migrate` on the same database at once.

`Database:ApplyMigrationsOnStartup` now defaults to **off**. The host asks what is pending and, if
anything is, shows the upgrade window instead of opening the books. Decline and the product closes —
running against a schema this build does not match is how a missing column becomes a crash halfway
through billing.

### The operator workflow

1. Everyone else closes the product. The window says so, because this is the part no software can
   enforce.
2. One person presses **Apply upgrade now**. The pending changes are listed by name — "3 changes
   pending" is nothing an operator can act on or report; a name is something they can read back over
   the phone.
3. The books open.

A failed upgrade leaves the message on screen with the books shut. Both buttons are driven by the
commands' `CanExecute` rather than a converter: a half-applied migration is the one state worth going
out of the way to prevent, and "the button was still clickable" is how it happens.

Being single-operator is procedural, not enforced. A server-side application lock would make it a
guarantee, and cannot be written against a provider this machine has no instance of.

## Developer notes

- `ConcurrencyTokens.Declare` walks the model for `IConcurrencyStamped` and marks the property;
  `Stamp` assigns a fresh value to every added and modified entry. Both are called from
  `AppDbContext`, and `Stamp` runs **after** the audit images are taken — stamping before capture
  would put a fresh token into every "after" image, so the edit log would show a change on every row
  that was merely re-saved.
- `ConcurrentUpdateException` is a domain type, not EF's `DbUpdateConcurrencyException`, because the
  Application and App layers have to catch a lost update and neither may reference EF Core
  (ADR-0001). `UnitOfWork` does the translation.
- `IVoucherNumberSequenceLock` is a port satisfied in Infrastructure because the lock must be held by
  the connection writing the voucher. The Dapper reporting path opens its own connection, so a lock
  taken through `ISqlQueryExecutor` would be released immediately and protect nothing — and on
  SQLite would deadlock against the transaction it was meant to guard.
- `IDatabaseInitialiser` splits into `GetPendingMigrationsAsync` (look) and
  `ApplyPendingMigrationsAsync` (do). The second ignores the startup switch on purpose: the switch
  answers "may this happen unattended?", and the answer being no is why somebody is invoking it by
  hand.

## SQL Server Express — not delivered

C9.4 is unmet and the provider is not written. The reason and the measurement that was done instead
are in `specs/phase-9-multiuser.md` §4c; the short version is that nothing about it can be executed
on a machine with no SQL Server, LocalDB or Docker (`open-questions.md` #24), and the Phase 0 dialect
abstraction has been shown to hold — the SQLite-only surface is eight occurrences across four
construct kinds in roughly seven hundred lines of SQL, pinned by `SqlServerPortabilityTests`.

## Verification

- `ConcurrencyTokenTests` — the token on every mutable entity and on nothing else, that it rotates on
  write, that two scopes editing the row they both loaded end with the second refused and the first
  write surviving, that the message names what to reload, and that a scope saving the same entity
  twice is not mistaken for a conflict.
- `VoucherNumberAllocationTests` — consecutive numbers with no gap, per-financial-year counting, a
  genuine eight-way race on one database file issuing no number twice, the lock leaving both the mark
  and the token alone, and the stale-counter case a single-user build could never show.
- `GatedMigrationTests` — the default being off, the switch honoured both ways, the explicit apply
  working regardless of it, and a second apply doing nothing.
- `SqlServerPortabilityTests` — the divergence inventory, and the absence of any construct without a
  mechanical T-SQL equivalent.
