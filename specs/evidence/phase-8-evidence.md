# Phase 8 evidence — Data Interoperability (Tally & Excel)

**Spec:** `specs/phase-8-interoperability.md` (Approved 24 Sep 2026)
**Filed:** 24 September 2026

---

## 1. Checkpoint results

| # | Criterion | Status | Evidence |
|---|---|---|---|
| C8.1 | A company's masters + transactions exported from this app import cleanly into a real (or sandboxed) TallyPrime company without errors | **Not met — needs a real Tally** | See §4 |
| C8.2 | Round-tripping export → import of the same data does not duplicate records | **Met** | `InteropWorkflowTests.Import_OfThisCompanysOwnExport_ChangesNothing` (XML and JSON), `…_CountsEverythingAsAnUpdate`, `…Import_OfAnUnchangedVoucher_AddsNoRevision` |
| C8.3 | An Excel item import with a non-standard column order succeeds via the mapping step | **Met** | `ExcelWorkbookTests.Import_ItemSheetInANonStandardColumnOrder_ReachesTheItemMaster`, `ExcelInteropSourceTests.ReadAsync_ItemsInANonStandardColumnOrder_ImportsThemAnyway` |
| C8.4 | An import with one invalid row commits nothing and reports the offending row and reason | **Met** | `InteropWorkflowTests.Import_WithOneUnbalancedVoucher_CommitsNothingAndNamesTheRow`, `…Import_ReportsEveryProblemInOnePass` |
| C8.5 | Amounts and quantities survive a round-trip with no precision loss | **Met** | `TallySerialisationTests.RoundTrip_AmountsAndQuantitiesAtFullScale_LoseNoPrecision` (both formats), `InteropWorkflowTests.Import_AmountsAtFullScale_ReachTheBooksExactly` |
| C8.6 | After importing a Tally export, the trial balance and closing stock valuation in this app equal those in the source company | **Met against our own export; not met against real Tally data** | `InteropWorkflowTests.Import_IntoACompanyThatHasNeverSeenTheData_ReproducesTheBooks` |

### On C8.2

Two tests, because the criterion has two halves that fail differently. The census test proves no rows
appeared. The tally test proves the importer *knew* they would not: a file whose records it believed
were all new, that happened to leave the row count alone because every write collided with a unique
index, would pass a census and be one schema change away from duplicating the accountant's books.

A third test covers the cost of getting idempotence right the lazy way — altering every voucher on
every re-run would add a revision to each one (ADR-0002) and fill the edit log with changes that
changed nothing.

### On C8.6

The test runs the real migration: a second SQLite database that has never seen the data, an import,
and then the origin's trial balance and closing stock compared against the target's. The
reconciliation the import performs on itself agrees, and reports that the source declared no totals
of its own — which is true of every Tally export, since the format has no element for one.

What this does not do is compare against figures produced by Tally. That is the half of the criterion
that waits on a real file.

---

## 2. Test run

```
.\build.ps1
```

```
Passed!  - Failed: 0, Passed: 485, Skipped: 0, Total: 485 - VinayakaAgencies.Domain.Tests.dll
Passed!  - Failed: 0, Passed: 112, Skipped: 0, Total: 112 - VinayakaAgencies.Application.Tests.dll
Passed!  - Failed: 0, Passed: 160, Skipped: 0, Total: 160 - VinayakaAgencies.App.Tests.dll
Passed!  - Failed: 0, Passed: 249, Skipped: 0, Total: 249 - VinayakaAgencies.Infrastructure.Tests.dll

=== Done (build + test succeeded) ===
```

**1,006 tests, 0 failures, 0 warnings** under warnings-as-errors.

Phase 8 added 93: 40 in Application (the serialisers, the readers' behaviour on bad input, the Excel
mapping rules), 20 in Infrastructure (the database claims and the ClosedXML adapters against real
`.xlsx` files), 27 in App (the three screens' own rules, including progress reporting), and the
guard-set updates in Domain.

### One intermittent failure, and what was done about it

On one run, a single Infrastructure test failed inside `RelationalConnection.OpenAsync` — a SQLite
connection that would not open. No assertion was involved; the test never reached one.

`SqliteTestHost` gave every test its own database *file* inside one shared temp directory, and xUnit
runs test classes in parallel. Phase 8 added a second class that writes temp files into the same
place, which is new pressure on it. Each host now gets its own **directory** and removes it on
dispose.

Three consecutive runs of the Infrastructure suite are clean since. That is suggestive rather than
proof — the failure was rare to begin with, so three clean runs would be the expected outcome either
way. It is recorded because an intermittent failure nobody writes down is one that gets blamed on the
next change. If it recurs, the next step is to serialise the two file-writing suites.

### The Phase 8 integration tests

```
Interop.InteropWorkflowTests
  Import_OfThisCompanysOwnExport_ChangesNothing(format: TallyXml)
  Import_OfThisCompanysOwnExport_ChangesNothing(format: TallyJson)
  Import_OfThisCompanysOwnExport_CountsEverythingAsAnUpdate
  Import_OfAnUnchangedVoucher_AddsNoRevision
  Import_IntoACompanyThatHasNeverSeenTheData_ReproducesTheBooks
  Import_IntoANewCompany_CreatesTheMastersItNames
  Import_AmountsAtFullScale_ReachTheBooksExactly
  Import_WithOneUnbalancedVoucher_CommitsNothingAndNamesTheRow
  Import_NamingALedgerThatDoesNotExist_ReportsItRatherThanCreatingOne
  Import_ReportsEveryProblemInOnePass
  Import_MasterCreatedEarlierInTheSameFile_SatisfiesALaterReference
  Import_SerialTrackedItem_WarnsWithoutBlocking

Interop.ExcelWorkbookTests
  ReadRows_AfterWritingASheet_ReturnsWhatWasWritten
  ListSheets_NamesEverySheetInTheFile
  Import_ItemSheetInANonStandardColumnOrder_ReachesTheItemMaster
  Import_ItemSheetWithHsnCodes_RecordsThemAsMasters
  Suggest_ColumnsNamedCloseToTheirFields_AreMatchedWithoutBeingAsked
  SaveMapping_NamingAFieldTheTargetDoesNotHave_IsRefused
  SaveMapping_ThenReload_KeepsItsColumns
  Export_ThenImportTheSameWorkbook_NeedsNoHandBuiltMapping
```

---

## 3. Smoke run

Run against a **copy** of the working database from `%LOCALAPPDATA%\VinayakaAgencies`, not the file
itself. The point of the exercise is that the phase's migration applies to a database with real rows
already in it, and taking a copy gets that without putting the owner's data in the path of a test.

```
MOTORPUMP_Database__ConnectionString="Data Source=…\phase7-copy.db" dotnet run --project src\VinayakaAgencies.App
```

```
2026-09-24 13:58:31.469 [INF] Applying migration '20260921043012_AddTdsAndBanking'.
2026-09-24 13:58:31.556 [INF] Applying migration '20260921045412_AddPayroll'.
2026-09-24 13:58:31.623 [INF] Applying migration '20260924051041_AddExcelColumnMappings'.
2026-09-24 13:58:33.154 [INF] Shell window opened.
```

The copy was older than expected and took three migrations rather than one, which makes the run
better evidence than intended: the Phase 8 migration applied on top of two others in the same
startup, against populated tables, and the shell still opened.

**Zero `[ERR]` or `[FTL]` rows** in `log-20260924.txt` for the run. Both new tables are present in the
migrated file:

```
ExcelColumnMappings
ExcelColumnMappingColumns
```

The owner's own `motorpump.db` was not opened and is unchanged. It will take the same migration the
next time they start the application, which is the normal path.

---

## 4. What is not proven, and why

### C8.1 needs a TallyPrime

Nothing here has been through Tally. Every encoding rule the format imposes is written from the
published specification and is unverified on real data: the amount sign, the date format, the
quantity-with-unit shape, the `.LIST` array convention in JSON, the `@` prefix on attributes, and the
assumption that Tally reads a single envelope's messages in document order.

They are collected in `TallyConventions` and `TallyXmlNames` for exactly this reason. When a real file
arrives, each is a one-line fix in one file rather than a hunt through a dozen.

The owner's own books have not been supplied either — `open-questions.md` #22 — so the importer is
proved against fixtures derived from this application's own exports. That proves we can read back our
own assumptions, which is strictly less than it sounds, and is the same position Phase 6's
`IIrpClient` is in against the NIC sandbox.

**What would close it:** one month exported from here, imported into the accountant's TallyPrime, and
a handful of figures checked both sides. And one export *from* their Tally, run through Import here.

### Owner walkthrough and screenshots

Three new screens — Export, Import, Column mappings — have not been walked through by the owner and
no screenshots are filed.

### Cost-centre naming

`open-questions.md` #18 — unique per company or per category — is now live, because Tally import
matches cost centres by name (§4a D4). Nothing is blocked, but it should be closed before the first
real import runs: a migration is precisely when duplicate names arrive from outside.

---

## 5. Defects found and fixed during the phase

### A Phase 2 defect: stock valuation on an empty ledger

`StockReportService.ReconcileValuationAsync` threw on a company with no stock movements. Dapper builds
its deserialiser from the reader's schema before it knows whether any rows came back, and on an empty
result SQLite reports no type for an aggregate expression — so the positional `ValuationSqlRow` was
asked for a `byte[]` constructor and found none.

Every other report in the suite returns a row per master and never reaches the condition, which is why
it survived Phase 2's own tests. It surfaced here because the import reconciles against a fresh
company. Left alone it would have appeared as the Stock Valuation screen throwing on a new install.

Fixed by mapping that row by property rather than by constructor, which defers the type conversion to
a row that actually exists. Recorded in the spec at §4b B4.

### A design gap: voucher types had to become masters

The exporter wrote a voucher's own type name and the reader rejected anything that was not one of
Tally's eight built-ins — which "Stock Journal", seeded by Phase 2, is not. Inferring a behaviour from
an unrecognised name was rejected: guess right and nothing happens, guess wrong and real money posts
down the wrong path with nothing in the file to show it.

Voucher types are now exported as masters declaring their behaviour through the built-in they inherit,
which is how Tally expresses the same thing and is equally needed for C8.1. Recorded at §4b B1.

### The entity guard caught a duplicate name

`InteropRecord` had a nested `StockItem`, a second name for Phase 2's approved `Item`.
`NoBusinessLogicGuardTests` failed on it, correctly. The canonical model now uses this application's
vocabulary throughout and only the serialiser knows Tally calls it `STOCKITEM`.

---

## 6. Schema

One migration: `AddExcelColumnMappings` — `ExcelColumnMappings` and `ExcelColumnMappingColumns`. §2
already requires mappings to be saveable and reusable, so this is the spec's own requirement; it is
the only schema the phase adds, because everything else reads and writes files.

One new reporting query, `Interop.JournalTotals`, for the C8.6 reconciliation (§4b B2).
