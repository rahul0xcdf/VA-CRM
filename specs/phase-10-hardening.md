# Phase 10 — Hardening & Packaging

```yaml
Phase: 10
Title: Hardening & Packaging
Status: Approved
Approved: 14 Oct 2026 (project owner)
Source: project.md §10 (Phase 10)
Depends on: Phases 0–9
```

> **Approved 14 Oct 2026.** The owner approved the phase and directed that work start, naming the
> installer as the thing they want first — *"For other device I want the installation setup and all
> an installer to setup everything"* — and then *"Cover all those and implement all those
> checkpoints"*, so all six criteria in §3 are in scope, not C10.2 alone.

## 1. Deliverables (project.md §10)

Backup/restore, data-at-rest protection, performance/indexing pass, Windows installer.

## 2. Scope notes

- **Backup/restore:** scheduled and on-demand; the backup covers the database **and** the FastReport
  templates, logos and configuration — a restore that loses the customised invoice template is not a
  restore. Backups are verified (restore into a scratch DB and check integrity), not merely written.
- **Data at rest:** encrypted credentials (NIC/GSTN), and a decision recorded on whether the company
  database itself is encrypted, with the recovery consequences spelled out.
- **Performance/indexing pass** against a realistic dataset — the ≥5,000-SKU catalogue from Phase 2
  plus several years of vouchers. Target the screens that are used hundreds of times a day: item
  lookup during billing, customer outstanding, day book.
- **Windows installer** (MSI or MSIX): prerequisites, per-machine install, upgrade path that migrates
  an existing database without data loss, and clean uninstall that does **not** delete company data.
- Crash reporting / log collection the owner can actually send when something goes wrong.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C10.1 | A full backup/restore cycle round-trips without data loss |
| C10.2 | The installer runs clean on a fresh Windows machine |

Additional:

| # | Criterion |
|---|---|
| C10.3 | An upgrade install over the previous version preserves all company data and applies pending migrations |
| C10.4 | Uninstall leaves company data and backups intact |
| C10.5 | Named hot screens meet their response budget on the realistic dataset |
| C10.6 | A restore from backup reproduces a byte-identical trial balance and stock summary |

## 4. Open questions — closed 14 Oct 2026

All four are answered in §4a. D1 and D2 close the two backup questions, D3 the response budgets,
D4 the signing question. Four answers are **working answers the owner has not yet seen the
consequences of in use** and are flagged for confirmation at walkthrough: the retention count (D1),
the budget numbers (D3), the decision not to encrypt the database file (D5), and the data
relocation (D7).

## 4a. Decisions (14 Oct 2026)

### D1 — Backups go to local disk by default, with an optional second destination

The default destination is a `backups` folder beside the company database, because a backup that
needs configuration before it works is a backup that does not exist on the day it is needed. A
second destination — a network share, or an external drive — is a settings field, and when it is set
every backup is written to both. Failing to reach the second destination is reported but does **not**
fail the backup: losing the off-machine copy is bad, losing the local one because a USB drive was
unplugged is worse.

**Retention: the last 30 verified backups, and never the last one.** Thirty is a working answer
chosen to match the existing 31-day log retention, not a considered figure — a counter posting fifty
invoices a day produces a database measured in tens of megabytes, so thirty copies is not a disk
concern for years. The floor matters more than the number: pruning must never be able to empty the
folder, so the most recent backup is exempt from every retention rule.

**Confirm at walkthrough:** the retention count, and whether a weekly/monthly tier is wanted on top
of the daily one.

### D2 — No cloud or off-site backup in v1

`project.md` §1 puts cloud/remote access out of scope for v1 and nothing in this phase is a reason to
reopen it. D1's second destination is the answer to off-site: a network share, or an external drive
the owner carries home.

**The consequence is stated plainly because it is the owner's risk to accept:** with only the default
destination configured, fire, theft or a dead disk takes the books with the machine. The backup
screen says so, and it reports the age of the newest backup at the second destination, so "I thought
it was backing up" cannot survive a glance at the screen.

### D3 — Response-time budgets for the hot screens

Measured at the service seam against the realistic dataset of §2, warm, on the development machine:

| Screen / operation | Budget | Why this number |
|---|---|---|
| Item lookup during billing (`LookupBox` keystroke) | **300 ms** | Typed into on every line of every invoice. Past ~300 ms the operator outruns the list and starts distrusting it. |
| Customer outstanding / statement | **1.5 s** | Opened on demand, a few times an hour; an operator expects a report to take a moment. |
| Day book | **1.5 s** | Same shape — a report, not a keystroke. |
| Trial balance | **2 s** | Whole-book aggregate, run at close rather than at the counter. |

These are working numbers. The load-bearing one is the lookup budget, the only one on the critical
path of billing; the rest are generous on purpose, so failing them means a real regression rather
than a slow morning. Budgets are asserted by a test, so "it felt fine" is never the evidence — but a
timing test on a shared machine is noisy, so the assertion is at the budget while the **measured**
figure is recorded in the evidence file for comparison.

**Confirm at walkthrough:** whether 300 ms matches what the counter actually feels.

### D4 — The installer ships unsigned at launch, with the signing step already wired

Signing needs a code-signing certificate the owner has to buy, and an OV certificate's SmartScreen
reputation is earned over downloads rather than granted at purchase — so buying one does not remove
the warning on day one either. Since this installer is carried to a handful of machines on a USB
stick rather than downloaded, the warning is a one-time *More info → Run anyway* per machine, and
that is documented in the user guide so the operator is not surprised by it.

The build takes a certificate thumbprint from the environment and signs when one is present, so
buying a certificate later is a variable, not a rebuild. Nothing about the package shape changes.

### D5 — The company database file is **not** encrypted; the credentials inside it stay encrypted

§2 asks for a decision with its recovery consequences, so here it is, with the reason and not only
the answer.

**What is already protected:** the NIC/GSTN credentials are DPAPI-encrypted (Phase 6, C6.6) and user
passwords are PBKDF2 one-way hashes (Phase 9, D2). Those stay protected, and the Phase 6 credential
scan is extended in this phase to cover **backup archives** as well as the live database file — an
encrypted secret written to a backup in the clear is not an encrypted secret.

**What is not protected, and why:** whole-database encryption (SQLCipher, or SQL Server TDE from
Phase 9) is declined for v1. The threat it defends against is someone who already has the machine or
the backup drive. Against that threat it buys real protection — and it costs the ability to recover.
A key held on the same machine protects nothing; a key held off it means a forgotten passphrase is
indistinguishable from a dead disk, and the books are statutorily required for eight years under
Rule 3(1). For a single shop whose real exposure is a stolen machine, Windows **BitLocker** gives the
same protection at the disk level with a recovery key Microsoft escrows against the owner's account —
a better answer than anything this application can implement, and one the diagnostics screen reports
the state of so an operator can see whether it is on.

**The consequence, stated plainly:** anyone who obtains the database file or a backup can read every
customer, price and voucher in it. They cannot read the IRP credentials and cannot recover a
password. If the backup drive leaves the premises, BitLocker-encrypt it.

**Confirm at walkthrough:** the owner may prefer encryption despite the recovery cost. It is a
provider-level change rather than a rewrite, and it is cheapest to make before the first backup
exists.

### D6 — MSI built with WiX v5, and a self-contained publish

MSI rather than MSIX. MSIX wants a signing certificate to install at all — which D4 has just
declined — and its per-user, sandboxed install model fights both the per-machine install §2 asks for
and the shared `%ProgramData%` data location of D7. WiX v5 builds from a `.wixproj` through the
ordinary `dotnet build`, so the installer is a project in this repository that restores from NuGet
like everything else, rather than a tool somebody has to install and a GUI somebody has to remember
how to drive.

**The app is published self-contained (`win-x64`).** The alternative is a framework-dependent build
plus a .NET 8 Desktop Runtime prerequisite the installer downloads — which is exactly the step that
fails on the fresh machine of C10.2 if that machine has no internet. Self-contained costs about
70 MB in the package and removes the prerequisite entirely: the right trade for an installer carried
to a counter PC on a USB stick.

### D7 — Company data moves to `%ProgramData%`, and this is a defect fix rather than a preference

Phases 0–9 put the database at `%LOCALAPPDATA%\VinayakaAgencies\motorpump.db`. `LOCALAPPDATA` is
**per Windows user**. A counter PC where two staff sign in under two Windows accounts would therefore
keep **two different sets of books**, each invisible from the other, and neither would look broken —
the second user would simply be walked through first-run company creation and start billing into an
empty company. That is a data-loss defect waiting for the day a second person logs in, and the
per-machine install this phase asks for is what makes it reachable.

Company data moves to `%ProgramData%\VinayakaAgencies\` — database, backups, logo and local
configuration. That location is machine-wide, survives user profile changes, and is where the
installer can set an ACL granting `Users` modify rights once. **Logs stay per-user** under
`%LOCALAPPDATA%`, which is correct: they describe what one operator did.

**Existing installs are relocated on first run after upgrade**, not abandoned: the old path is moved
to the new one during startup, before anything opens the database, and a marker is left behind so it
cannot run twice. An upgrade that finds no old database does nothing. This is what C10.3 proves.

## 4b. Changes beyond spec

### B0 — no schema change at all

Worth stating, because every phase since Phase 1 has added a migration and the absence of one here
is a fact a reviewer should not have to establish for themselves. Phase 10 adds **no entity, no
column and no migration**. Backups are files, the manifest is JSON inside the archive, and the
retention policy is configuration. The only persisted state the phase touches is the database file's
*location* (D7), which is not schema.

### B1 — `IAppDataPaths`, `AppDataPaths` and `LegacyDataRelocator`

Three new application-layer types that D7 needs. The resolver signature changed with them:
`SqliteConnectionStringResolver.Resolve(DatabaseOptions)` became
`Resolve(DatabaseOptions, IAppDataPaths)`, and gained `ResolveDatabaseFilePath` so the backup
service can snapshot a path rather than reconstruct one. All three are `internal` to
Infrastructure, so no public contract moved.

`App.ConfigureLogging` now reads its directory from `IAppDataPaths` too. Phases 0–9 derived the same
location independently in two places — the SQLite resolver and the Serilog configuration — and D7
moved one of them; two derivations of one path is how a diagnostics bundle ends up collecting logs
from a directory nothing writes to.

### B2 — `BackupOptions` crosses the layer boundary as a plain object, not `IOptions<T>`

`BackupService` is the first application-layer service to take configuration at all. The layer
carries only `Microsoft.Extensions.DependencyInjection.Abstractions` by design — no service there
references `IOptions<T>`, and adding `Microsoft.Extensions.Options` to it would be a new dependency
on the layer that is supposed to declare needs rather than take them on. So the section is bound in
`AddInfrastructure`, where the configuration system already lives, and unwrapped to the settings
object itself on the way across:

```csharp
services.AddScoped(provider => provider.GetRequiredService<IOptions<BackupOptions>>().Value);
```

### B3 — the archive is an ordinary ZIP, and that is a decision rather than a default

A proprietary container would be marginally harder to tamper with. It would also mean an owner whose
application will not start has no route to their own books. With a ZIP the recovery path of last
resort is to open the file in Explorer and take the database out of `database/`. That path is
documented in the user guide, and it is worth more than the obscurity it costs.

### B4 — the book fingerprint is financial-year scoped, not a single as-on date

**This is the one place where the obvious implementation was wrong, so it is recorded rather than
left in a code comment.** C10.6 compares a trial balance across a restore, and the natural way to
write that is to ask for one as on a date far in the future. That does not work:
`GetTrialBalanceAsync` accumulates from the start of the financial year *containing* the date it is
given, so a far-future date yields a statement for a year with nothing in it. A fingerprint built
that way hashes an empty report, matches itself after any restore whatsoever, and carries no
information at all — a test that would have passed against a `RestoreAsync` that did nothing.

`DateOnly(9999, 12, 31)` also overflowed `FinancialYear.RangeFor`, which is how the design error
surfaced at all. The fingerprint now reads `IFinancialYearProvider.ListAsync()` and renders one
trial balance per year on record, oldest first; stock on-hand is cumulative and is taken once, at
the latest year end. A multi-year Phase 8 migration is therefore checked year by year, which is
better than the single statement originally intended.

### B5 — `DiagnosticsBundleService` depends on `IEinvoiceSettingsService`

A cross-feature dependency inside the application layer, taken deliberately. D5 extends C6.6's
credential scan to the diagnostics bundle, and Phase 6's scrub is **two** passes: one by field name,
one by exact secret value. Only the first works without knowing the secrets. Since the bundle is the
one artefact designed to be emailed to a stranger, it resolves the stored credentials so the value
pass runs too — which is what catches a secret quoted back inside an error message under a field
name nobody anticipated. Failure to resolve (DPAPI is per Windows account) degrades to the key pass
rather than refusing to produce a bundle.

### B6 — three unhandled-fault handlers where Phases 0–9 had one

`DispatcherUnhandledException` covered the UI thread. A fault on a background thread bypasses the
dispatcher entirely, and a faulted `Task` nobody awaited surfaces later still, at a garbage
collection, with no useful stack unless it is captured. `AppDomain.CurrentDomain.UnhandledException`
and `TaskScheduler.UnobservedTaskException` are now wired as well. The third is deliberately
non-fatal — it is the one unhandled fault that is not fatal by default and nothing is known to be
mid-write — while the first two still let the process die, which is the Phase 0 position restated:
a crash with a logged cause beats a half-posted voucher.

### B7 — the installer project is outside the solution

`installer/VinayakaAgencies.Installer/*.wixproj` is not in `VinayakaAgencies.sln`. It can only build
after a publish has staged the application, so including it would make `dotnet build` on the
solution fail from a clean checkout. `build.ps1` is unaffected; `installer/build-installer.ps1`
drives the two steps in order.

### B8 — four ICE checks suppressed in the installer, with reasons

ICE61 (fires by definition with `AllowSameVersionUpgrades`; WiX's own documentation says to suppress
it) and ICE38/ICE43/ICE57 (all written for per-user installs, all demanding an HKCU key path that a
per-machine installer must not write). Recorded in the `.wixproj` beside the suppression. Noted here
because suppressing validation deserves to be visible: ICE57 and ICE90 each caught a real mistake in
the first draft, and ICE38/43/57 were suppressed only after the shortcuts were restructured onto the
executable's own component — not instead of restructuring them.

## 4b-1. Defects found

### A pre-existing data-loss defect in Phases 0–9: the books were per Windows user

Not a Phase 10 defect — a Phase 0 one, live in every build since, and the reason D7 exists. The
database sat at `%LOCALAPPDATA%\VinayakaAgencies\motorpump.db`, and `%LOCALAPPDATA%` is per Windows
user. A counter PC where two staff sign in under two Windows accounts would have kept **two separate
sets of books**, each invisible from the other, and **neither would have looked broken**: the second
user would simply have been walked through first-run company creation and started billing into an
empty company, with the real books sitting untouched in a folder nothing read.

It had not bitten because Phases 1–9 were exercised on one developer machine under one account. The
per-machine install this phase adds is exactly what makes it reachable. Fixed by moving company data
to `%ProgramData%` with an explicit `Users` ACL, and by `LegacyDataRelocator`, which moves an
existing install's books across on first launch and refuses to merge when it finds two.

### Two defects in this phase's own code, caught by its own tests

Recorded because both were invisible to a passing build and would have shipped a backup feature that
appeared to work.

**A pooled SQLite handle kept the snapshot file locked.** `Microsoft.Data.Sqlite` returns a disposed
connection to its pool and keeps the underlying handle open, so the staged snapshot could not be
hashed into the archive and the staging directory could not be deleted. `Pooling = false` on both
one-shot connections. Caught by the first run of `BackupRestoreTests`.

**The integrity check threw instead of reporting.** A damaged database does not fail at open —
SQLite reads no page until asked — so `PRAGMA integrity_check` raised "file is not a database"
instead. An integrity check whose answer to a corrupt archive is an exception is not a check, and
the tamper test was what asked the question. Every SQLite call in `CheckIntegrityAsync` is now inside
one `catch (SqliteException)`.

## 4c. Not delivered

### C10.2, C10.3 and C10.4 need a fresh Windows machine

The MSI builds, and `installer/verify-installer.ps1` asserts nineteen structural facts about it
without installing anything — the executable and the bundled runtime are present, there is no .NET
launch condition, the data folder is under `CommonAppDataFolder` and is `Permanent`, no installed
file lives beneath it, an `Upgrade` row exists, `ALLUSERS=1`, both shortcuts are authored. That is
most of the ways an installer fails on a fresh machine, caught before the owner meets them.

It is not the checkpoint. **C10.2 asks that the installer run clean on a fresh Windows machine**, and
C10.3/C10.4 ask about a real upgrade and a real uninstall. Those are three actions on a machine this
repository does not have, and installing a per-machine MSI on the development machine would neither
be a fresh machine nor be reversible. They are the owner's step, listed in the evidence file as such.
Nothing above substitutes for it.

### The three fault handlers are not themselves tested

Each is a process-level event; a test that genuinely triggered an `AppDomain` unhandled exception
would take the test host down with it. They are three registration lines calling one method that
*is* covered. Flagged rather than left implied.

## 5. Not in scope

- Cloud or off-site backup (D2).
- Whole-database encryption (D5) — declined for v1 with reasons; reopenable.
- A Windows service or scheduled task that runs while the application is closed. The scheduled
  backup runs inside the application, on a timer and on clean shutdown. A day nobody opened the
  application is a day with nothing to back up.
