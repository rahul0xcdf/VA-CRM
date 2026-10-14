# Phase 10 — Hardening & Packaging: evidence

```yaml
Phase: 10
Spec: specs/phase-10-hardening.md (Status: Approved, 14 Oct 2026)
Filed: 14 Oct 2026
Verdict: C10.1, C10.5 and C10.6 met. C10.2, C10.3 and C10.4 built and structurally
         verified, but NOT met — all three need a fresh Windows machine.
```

## 1. Verdict per checkpoint

| # | Criterion | Verdict | Proven by |
|---|---|---|---|
| **C10.1** | A full backup/restore cycle round-trips without data loss | **Met** | `BackupRestoreTests` — 12 tests, §3 |
| **C10.2** | The installer runs clean on a fresh Windows machine | **Not met** | MSI builds and passes 19 structural checks (§4); the fresh machine is the owner's step |
| **C10.3** | An upgrade install preserves all company data and applies pending migrations | **Partially met** | Data half proven two ways (§5); the upgrade install itself needs a machine |
| **C10.4** | Uninstall leaves company data and backups intact | **Partially met** | Both data components verified `Permanent` in the built MSI; the uninstall needs a machine |
| **C10.5** | Named hot screens meet their response budget on the realistic dataset | **Met** | Measured figures, §6 |
| **C10.6** | A restore from backup reproduces a byte-identical trial balance and stock summary | **Met** | `Restore_AfterTheBooksMoveOn_…`, §3 |

Three of six met, two partially, one not. The three that fall short all fall short for the same
reason and it is not a code gap: installing, upgrading and uninstalling a per-machine MSI are
actions on a computer, and this repository does not have a spare one. §7 says exactly what the owner
has to do.

## 2. Build

```
=== Restore ===
=== Build (Debug) ===
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

`TreatWarningsAsErrors` is on for every project (`Directory.Build.props`), so zero warnings is the
build gate rather than a nicety.

## 3. Test run

`.\build.ps1`, 14 Oct 2026:

```
--- VinayakaAgencies.App.Tests ---
Passed!  - Failed: 0, Passed: 195, Skipped: 0, Total: 195, Duration: 12 s
--- VinayakaAgencies.Application.Tests ---
Passed!  - Failed: 0, Passed: 125, Skipped: 0, Total: 125, Duration: 98 ms
--- VinayakaAgencies.Domain.Tests ---
Passed!  - Failed: 0, Passed: 485, Skipped: 0, Total: 485, Duration: 217 ms
--- VinayakaAgencies.Infrastructure.Tests ---
Passed!  - Failed: 0, Passed: 351, Skipped: 0, Total: 351, Duration: 2 m 3 s

=== Done (build + test succeeded) ===
```

**1,156 tests, 0 failures.** Phase 9 filed 1,117; Phase 10 adds 39 (12 backup/restore, 5 hot-path,
11 relocator, 2 credential scans, and 9 absorbed by the App screen sweeps as the new screen was
picked up by `EveryScreenAgainstARealDatabaseTests`, `ScreenLoadReachabilityTests`,
`ScreenViewLoadTests` and `EveryWindowInTheApplication_LoadsWithTheApplicationResources`).

### C10.1 and C10.6 — the backup/restore suite

`tests/VinayakaAgencies.Infrastructure.Tests/Maintenance/BackupRestoreTests.cs`, all against a real
SQLite file through the real DI composition:

```
Passed  Create_OfABookWithPostingsAndStock_WritesAVerifiedArchiveCarryingTheDatabase [411 ms]
Passed  Restore_AfterTheBooksMoveOn_ReproducesTheBackedUpTrialBalanceAndStockSummary [738 ms]
Passed  Restore_KeepsASafetyCopyOfTheDatabaseItReplaced                              [1 s]
Passed  Restore_PutsBackTheOperatorOwnedFilesAsWellAsTheDatabase                     [848 ms]
Passed  Verify_WhenAnArchivedFileIsAltered_ReportsTheEntryThatNoLongerMatches        [816 ms]
Passed  Verify_WhenAnArchivedEntryIsMissing_SaysSoRatherThanPassing                  [3 s]
Passed  Restore_WhenTheArchiveFailsVerification_RefusesAndLeavesTheBooksAlone        [406 ms]
Passed  Restore_WhenTheArchiveNamesAMigrationThisBuildDoesNotKnow_Refuses            [418 ms]
Passed  Prune_WithRetentionSetToZero_StillKeepsTheNewestArchive                      [416 ms]
Passed  Prune_KeepsTheConfiguredNumberOfArchivesAndDropsTheOldest                    [909 ms]
Passed  Create_WhenASecondDestinationIsConfigured_WritesACopyThere                   [642 ms]
Passed  Create_WhenTheSecondDestinationCannotBeReached_StillKeepsTheLocalCopy        [1 s]
```

**C10.6 is `Restore_AfterTheBooksMoveOn_…`**, and its shape is the point. It fingerprints the books,
backs them up, **posts more vouchers and another stock receipt**, asserts the fingerprint has
genuinely changed (so the test cannot pass against a `RestoreAsync` that does nothing), restores, and
then requires the recomputed fingerprint to equal the original — hash, row counts and stock value.
The fingerprint is recomputed from the live books in a fresh scope, not read back out of the
manifest.

**Four of the twelve break an archive on purpose**, because a backup feature that writes a file and
reports success is easy and is not what an owner is asking about. One byte altered inside the
archived database is detected and the entry named; a deleted entry is reported rather than passed;
an archive that fails verification is refused for restore *and leaves the books untouched*; and an
archive claiming a migration this build does not know is refused with the migration named.

### C10.5 — the hot-path suite

```
Passed  ItemLookupDuringBilling_OnTheRealisticCatalogue_MeetsThe300MsBudget  [11 ms]
Passed  DayBook_OverAMonthOfTheRealisticBook_MeetsThe1500MsBudget            [67 ms]
Passed  TrialBalance_OverTheWholeLatestYear_MeetsThe2000MsBudget             [289 ms]
Passed  CustomerStatement_OverThreeYearsOfPostings_MeetsThe1500MsBudget      [376 ms]
Passed  TheDatasetIsActuallyRealistic                                        [< 1 ms]
```

`TheDatasetIsActuallyRealistic` is not padding: it asserts the fixture's shape, so a future edit
that shrank the dataset to make the suite faster fails there rather than silently weakening the
other four.

### D7 — the relocation

`tests/VinayakaAgencies.Application.Tests/Maintenance/LegacyDataRelocatorTests.cs`, 11 tests. Three
are refusals, which is where the data-loss risk actually sits: both locations holding a database
(refuses to merge, touches neither, names both paths), a legacy directory holding only logs (not an
install), and both roots resolving to one directory.

### D5 — the credential scan, extended

`tests/VinayakaAgencies.Infrastructure.Tests/Einvoicing/EinvoiceWorkflowTests.cs`:

```
Passed  StoredCredentials_NeverAppearInPersistedCallRowsOrTheAuditTrail   (Phase 6, C6.6)
Passed  StoredCredentials_RoundTripThroughTheProtector_ButAreNotStoredAsPlaintext
Passed  StoredCredentials_NeverAppearInABackupArchive                    (Phase 10, D5)
Passed  StoredCredentials_NeverAppearInADiagnosticsBundle                (Phase 10, D5)
```

The bundle test plants a secret **twice**: once under a credential-ish key in the settings file, and
once in plain prose in a log line under no key at all. The second plant is what makes the value pass
non-decorative — with the key-based pass alone that log line would leave the premises intact.

## 4. The installer

```
=== Publish (self-contained win-x64) ===
Staged 528 file(s).

=== Package (WiX v5 MSI) ===
Build succeeded.
    0 Warning(s)
    0 Error(s)

Installer : installer\VinayakaAgencies.Installer\bin\Release\VinayakaAgencies-Setup.msi
Size      : 63.67 MB
Version   : 0.1.0
```

`installer/verify-installer.ps1` reads the built MSI's tables and asserts nineteen facts:

```
=== Identity and upgrade path (C10.3) ===
ProductName    : Vinayaka Agencies
ProductVersion : 0.1.0
UpgradeCode    : {2D3FFA18-851E-41F2-967B-E33458FE88E7}
ALLUSERS       : 1
  [ok]   ProductName is "Vinayaka Agencies"
  [ok]   ProductVersion is set
  [ok]   ALLUSERS=1, so this is a per-machine install
  [ok]   An Upgrade table row exists, so an upgrade replaces the old version
  upgrade range  :  .. 0.1.0  -> WIX_UPGRADE_DETECTED
  upgrade range  : 0.1.0 ..   -> WIX_DOWNGRADE_DETECTED

=== Payload (C10.2, self-contained per D6) ===
Files in package : 528
  [ok]   The application executable is in the package
  [ok]   appsettings.json ships, so Backup defaults are present
  [ok]   hostfxr.dll present (self-contained host)
  [ok]   coreclr.dll present (the runtime is bundled)
  [ok]   WPF assemblies are bundled
  [ok]   Payload is a full self-contained publish, not a thin build
  [ok]   No .NET runtime launch condition (nothing to download on a fresh machine)

=== Company data folder (C10.4, D7) ===
  [ok]   A DataFolder directory is authored
  [ok]   A backups subfolder is authored
DataFolder parent : CommonAppDataFolder
  [ok]   DataFolder sits under CommonAppDataFolder (%ProgramData%), not a user profile
  [ok]   Component 'CompanyData' is Permanent, so uninstall leaves company data intact
  [ok]   Component 'CompanyBackups' is Permanent, so uninstall leaves company data intact
  [ok]   No installed files live under the data folder (the database is created at run time)

=== Shortcuts ===
  StartMenuEntry -> AppShortcutFolder
  DesktopEntry -> DesktopFolder
  [ok]   A Start Menu shortcut is authored
  [ok]   A Desktop shortcut is authored

=== Signature (D4) ===
Status : NotSigned
Unsigned, as recorded in phase-10-hardening.md section 4a D4.

=== All structural checks passed ===
Still outstanding for C10.2: an install on a genuinely fresh Windows machine.
```

The script's own last line is the honest summary. These checks cover most of the ways an installer
fails on a fresh machine — a missing executable, a runtime prerequisite that was supposed to be
bundled, a data folder that would take the books with it on uninstall — which turns the owner's
walkthrough into a confirmation rather than a first discovery. They are not the checkpoint.

## 5. C10.3's data half, proven two ways

The "preserves all company data" half of C10.3 does not depend on running the installer, and it is
proven twice over:

1. **The installer never owns the data.** `[ok] No installed files live under the data folder`
   above: the database is created by the application at run time, so the MSI has no file record
   beneath `%ProgramData%\VinayakaAgencies` to replace or remove. Both components there are
   `Permanent`.
2. **An upgraded install finds its books.** `LegacyDataRelocatorTests` covers the move from the
   pre-Phase-10 per-user location, including the refusal to merge when two sets exist.

What is *not* proven is that `msiexec` behaves as the tables say when run for real. That needs a
machine.

The **migration** half of C10.3 is the Phase 9 gated upgrade window, unchanged:
`ApplyMigrationsOnStartup` stays off, pending migrations are named on screen, and one operator
applies them deliberately. The installer does not touch the schema. Phase 9's evidence covers that
mechanism.

## 6. C10.5 — measured response times

Filed at `specs/evidence/phase-10-hot-path-timings.txt`, regenerable by setting `VA_EVIDENCE_DIR`
and running the suite.

```
Dataset: 5000 items, 6000 vouchers across 3 financial years,
         24 customers, 150 stocked items. Seed 20261014.

Customer statement (three years)   median     43 ms   (runs: 42, 43, 43, 48, 50)
Day book (one month)               median      7 ms   (runs: 7, 7, 7, 9, 9)
Item lookup during billing         median      1 ms   (runs: 0, 0, 1, 1, 1)
Trial balance (year)               median     34 ms   (runs: 31, 33, 34, 34, 36)
```

| Screen | Budget (§4a D3) | Measured | Headroom |
|---|---|---|---|
| Item lookup during billing | 300 ms | **1 ms** | 300× |
| Day book (one month) | 1,500 ms | **7 ms** | 214× |
| Trial balance (year) | 2,000 ms | **34 ms** | 59× |
| Customer statement (3 years) | 1,500 ms | **43 ms** | 35× |

Each figure is the median of five runs after two warm-up runs; the warm-up pays for the query plan
and the connection, and the median discards the one run that collided with something else on the
machine.

**The conclusion of the indexing pass is that no new index was needed.** The schema already carries
107 indexes from Phases 1–9, and at this volume every hot path is between one and three orders of
magnitude inside its budget. That is a finding, not an omission: the right outcome of a performance
pass is sometimes to measure, record the numbers, and change nothing. Had an index been added here
it would have been added on no evidence.

Two honest limits on the figures:

- **The dataset is 6,000 vouchers over three years** (12,000 journal lines) — roughly seven a working
  day. A busy counter posting fifty invoices a day would produce that in four months. The headroom
  is wide enough that a tenfold larger book would still clear every budget, but that is an inference
  from the numbers rather than a measurement.
- **Bill-wise outstanding (`BillwiseOutstandingAsync`, the C4.6 path) is not covered at volume.** It
  derives bills from posted *tax invoices*, and seeding thousands of those through the Phase 3
  pipeline was out of proportion to this phase. "Customer outstanding" in §2 is covered here by the
  customer **statement**, which walks the party ledger through the same reporting seam over three
  years of postings. Flagged so it is not read as covered.

## 7. Remaining before Phase 10 sign-off

**Owner actions that no amount of code can substitute for:**

1. **Install the MSI on a fresh Windows machine** — C10.2. Confirm: the SmartScreen warning behaves
   as the user guide describes; the wizard completes; the application starts and creates a company
   and owner account; no .NET prerequisite is requested.
2. **Upgrade install over the previous version** — C10.3. Install, create a company, post a voucher,
   then run the MSI again. Confirm the voucher is still there and that any pending schema upgrade is
   offered by the Phase 9 window.
3. **Uninstall** — C10.4. Confirm `C:\ProgramData\VinayakaAgencies` and its `backups` folder survive.
4. **The two-Windows-account check** (D7). On one machine, run the software as two different Windows
   users and confirm both open the *same* books. This is the defect §4b-1 describes, and it is worth
   seeing fixed rather than taking on trust.
5. **Walk the Backup & Restore screen** and take screenshots for this file (`specs/evidence/images`).

**Owner confirmations the spec flagged as working answers:**

6. **Retention count** — 30 archives (§4a D1). And whether a weekly/monthly tier is wanted.
7. **Response budgets** — in particular whether 300 ms matches what the counter actually feels
   (§4a D3). The measured 1 ms suggests the budget could be tightened considerably.
8. **The data-at-rest decision** — the database is deliberately *not* encrypted (§4a D5), with the
   recovery reasoning written out there and on screen. The owner may prefer encryption despite the
   cost; it is cheapest to change before backups are in circulation.
9. **The relocation** (§4a D7) — company data moved to `%ProgramData%`. A defect fix rather than a
   preference, but the owner should know their books moved.
10. **A code-signing certificate**, if the SmartScreen warning is unacceptable (§4a D4). Note that an
    OV certificate does not remove the warning on day one either; reputation is earned over
    downloads. The signing step is already wired.

**Not blocking Phase 10, but worth saying here:** the owner's "install on another device" may have
meant *sharing one set of books between two counters over the network*. That is Phase 9's C9.4,
which was deliberately stopped (`open-questions.md` #24 — no SQL Server on the development machine,
and a second provider's migrations need a solution-level change for the owner to approve). What
Phase 10 delivers is a standalone install plus a verified way to move books between machines. If
simultaneous billing from two counters is wanted, that is the next conversation.

## 8. Artefacts

| Artefact | Path |
|---|---|
| Spec with decisions | `specs/phase-10-hardening.md` (§4a D1–D7, §4b, §4b-1, §4c) |
| Hot-path timings | `specs/evidence/phase-10-hot-path-timings.txt` |
| Installer | `installer/VinayakaAgencies.Installer/bin/Release/VinayakaAgencies-Setup.msi` |
| Installer build / verify | `installer/build-installer.ps1`, `installer/verify-installer.ps1` |
| Feature docs | `docs/features/backup-and-restore.md`, `installer-and-packaging.md`, `diagnostics-and-crash-reporting.md` |
| User guide | `docs/user-guide/phase-10-install-and-backup.md` |
| Screenshots | **outstanding** — `specs/evidence/images` |
