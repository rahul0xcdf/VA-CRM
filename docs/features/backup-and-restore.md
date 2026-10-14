# Backup & Restore

**Phase 10** — `specs/phase-10-hardening.md` §2, checkpoints **C10.1** and **C10.6**.

Verified archives of the company database, the print templates, the logo and the operator's own
settings; and a restore that puts them all back and proves the books came back identical.

## What it does

A backup is one ZIP file holding:

| Inside the archive | What it is |
|---|---|
| `manifest.json` | When, by whom, on which machine, against which schema, and the book fingerprint |
| `database/motorpump.db` | A transactionally consistent snapshot of the live database |
| `data/…` | Everything else the operator owns under the data root — logo, `appsettings.Local.json` |

Print templates are **not** separate files: Phase 3 made them versioned rows (ADR-0004), so the
database snapshot already carries them. §2's "a restore that loses the customised invoice template
is not a restore" is satisfied by the database half, and `data/` covers the files beside it.

## User workflow

**Gateway → Administration → Backup & Restore.**

| Action | Key | What happens |
|---|---|---|
| Refresh | — | Lists every archive at the default destination, newest first |
| Back up now | `Ctrl+A` | Snapshot, archive, verify, copy to the second destination, prune |
| Verify | `Alt+F` | Reopens the selected archive and checks it end to end |
| Restore | `Alt+D` | **Arms** the restore — it does not perform it |
| Confirm restore | `Alt+D` again | Replaces the live books, after taking a safety copy |
| Cancel | `Alt+X` | Disarms |
| Collect diagnostics | — | See `diagnostics-and-crash-reporting.md` |

Restore is two keystrokes on purpose. It replaces the live books, so a single mis-selected row would
otherwise cost everything posted since that archive was taken. The action bar relabels once armed,
so the second press is never a surprise, and the rule lives in the view model where a unit test can
reach it without a window (ADR-0007).

The screen also states two things an operator must not have to be told verbally:

- **Whether an off-machine copy exists** (§4a D2). With no second destination set, the screen says
  so in warning colour: every copy of the books is on this one computer.
- **That the database is not encrypted** (§4a D5), and that a backup drive leaving the premises
  should be BitLocker-encrypted.

## Scheduled and automatic backups

| Trigger | Setting | Default |
|---|---|---|
| On clean shutdown | `Backup:BackupOnShutdown` | on |
| Verify every archive after writing | `Backup:VerifyAfterWrite` | on |
| Retention | `Backup:RetainedBackupCount` | 30, and **never the last one** |
| Second destination | `Backup:SecondDestination` | empty |

Shutdown is the one moment the books are known to be quiet and the operator known to be finished,
which is why it is the default. There is deliberately **no Windows service** (§5): a day nobody
opened the application is a day with nothing to back up.

## Developer notes

```
Application/Maintenance/
  IBackupService.cs / BackupService.cs        policy: what goes in, retention, verification, restore
  IBackupArchiveStore.cs                      port: the provider-specific mechanics
  BackupContracts.cs                          manifest, entry, summary, verification, outcome
  BookFingerprint.cs                          the C10.6 hash
  IBookFingerprintService.cs / …Service.cs    renders the two statements and hashes them
  BackupOptions.cs                            the settings above
Infrastructure/Maintenance/
  SqliteBackupArchiveStore.cs                 SQLite online backup, integrity check, file swap
```

Three things are load-bearing and easy to get wrong:

**The snapshot is not a file copy.** `SqliteBackupArchiveStore.SnapshotDatabaseAsync` uses SQLite's
online backup API. The database runs with a write-ahead log, so the committed state is split across
the main file and the `-wal` beside it; a file copy taken while a connection is open can catch a
page mid-write or miss an uncheckpointed log, and the archive restores into a database that fails
its integrity check — on the day it is needed, having looked fine for months.

**Both one-shot connections set `Pooling=false`.** Microsoft.Data.Sqlite returns a disposed
connection to its pool and keeps the file handle open. Without this the staged snapshot cannot be
hashed into the archive, and the scratch copy cannot be deleted. This was a real bug caught by the
first test run.

**The integrity check reports rather than throws.** A damaged file does not fail at open — SQLite
reads no page until asked — so "file is not a database" surfaces on the first pragma. Every SQLite
call in `CheckIntegrityAsync` is inside one `catch (SqliteException)` for that reason: a verifier
whose answer to a corrupt archive is an exception is not a verifier. Also a real bug, caught by the
tamper test.

### The book fingerprint (C10.6)

C10.6 asks that a restore reproduce a *byte-identical* trial balance and stock summary. The
fingerprint renders both to a canonical byte sequence and hashes it with SHA-256; equal hashes mean
the renderings were byte-for-byte the same.

**The trial balance is financial-year scoped**, and that shapes the whole design.
`GetTrialBalanceAsync` accumulates from the start of the financial year containing the date it is
given, so there is no single "as on" date meaning "the whole book" — a date far in the future
returns an *empty* statement, because the year it implies has no postings. A fingerprint built that
way would hash an empty report, match itself after any restore whatsoever, and carry no information
at all. (The first draft did exactly this and the overflow in `FinancialYear.RangeFor` is what
exposed it.) So the whole book is covered the only way it can be: one trial balance per financial
year on record, oldest first, concatenated. A multi-year Phase 8 migration is therefore checked year
by year. Stock on-hand is cumulative, so it is taken once, at the latest year end.

Canonicalisation rules, each of which exists because its absence would make a correct restore look
broken: rows ordered by GUID rather than query order; every figure at a fixed scale under
`InvariantCulture`; minor units hashed as stored (ADR-0003), never a converted decimal; and the
field separator is ASCII unit separator, which nothing typed at a keyboard can produce.

### Restore safety

1. The archive is **verified first** and a failure refuses the restore outright.
2. An archive naming a migration this build does not know is **refused**, with the migration named.
   Restoring a newer schema produces a program that runs and then fails on a missing column halfway
   through billing.
3. The live database is **copied aside before** being replaced, and the copy's path is reported — so
   "undo the restore" is a file copy somebody can be talked through on the telephone.
4. The `-wal` and `-shm` of the replaced database are **deleted**. Leaving them beside a different
   main file is how a restore turns into corruption.
5. The fingerprint is **recomputed from the restored books**, not read back out of the manifest.

The application must be restarted after a restore; the screen says so.

## Verification

`tests/VinayakaAgencies.Infrastructure.Tests/Maintenance/BackupRestoreTests.cs` — 12 tests against a
real SQLite file through the real DI composition. Four of them break an archive on purpose:

| Test | Proves |
|---|---|
| `Create_OfABookWithPostingsAndStock_…` | C10.1 — a verified archive carrying the database and a non-empty fingerprint |
| `Restore_AfterTheBooksMoveOn_…` | **C10.6** — books change, restore, fingerprint matches exactly |
| `Restore_KeepsASafetyCopy…` | The undo path exists and is itself a valid database |
| `Restore_PutsBackTheOperatorOwnedFiles…` | §2 — the logo and settings come back too |
| `Verify_WhenAnArchivedFileIsAltered_…` | One byte changed is detected and named |
| `Verify_WhenAnArchivedEntryIsMissing_…` | A deleted entry is reported, not passed |
| `Restore_WhenTheArchiveFailsVerification_…` | Refused, and the books are left untouched |
| `Restore_WhenTheArchiveNamesAMigration…` | The schema guard refuses and names the migration |
| `Prune_WithRetentionSetToZero_…` | §4a D1 — pruning can never empty the folder |
| `Prune_KeepsTheConfiguredNumber…` | Retention keeps the newest N |
| `Create_WhenASecondDestinationIsConfigured_…` | The off-machine copy is written |
| `Create_WhenTheSecondDestinationCannotBeReached_…` | Reported, never fatal — the local copy survives |

`StoredCredentials_NeverAppearInABackupArchive` in `Einvoicing/EinvoiceWorkflowTests.cs` extends the
Phase 6 C6.6 credential scan to backup archives (§4a D5): every entry is read as Latin-1 and scanned
for the stored NIC secrets.
