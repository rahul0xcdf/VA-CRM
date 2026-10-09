# Phase 1 Evidence Report — Accounting Core

```yaml
Phase: 1
Verified by: development agent, owner review pending
Verified on: 2026-10-05
Repo state verified: branch phase-7-tds-payroll-banking, at c37e563 plus this session's commits
Spec: specs/phase-1-accounting-core.md §14 (criteria), §15 (evidence required)
```

Phase 1 was implemented long before this file existed, and the status note in `CLAUDE.md` had
carried "the ≥500-voucher stress evidence" as outstanding ever since. This report closes that, and
closes the rest: **all twenty criteria are now met.** Eight of them were short of a test that §14
names and nobody had written — in one case (C1.14) because the operation the criterion describes
did not exist in the application at all. Each is now asserted; §1 says by what.

**Five pre-existing defects were found while producing this evidence**, every one of them invisible
to the suite as it stood. They are in §6. The last was found only by running the product.

---

## 1. Checkpoint results

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| C1.1 | A balanced voucher updates trial balance, balance sheet and P&L | **Met** | `PostingWorkflowTests.Post_BalancedPayment_UpdatesEveryDerivedStatement` — all three statements asserted line by line against hand-computed figures |
| C1.2 | An unbalanced voucher is rejected | **Met** | `PostingWorkflowTests.Post_UnbalancedByOneMinorUnit_IsRejectedWithoutConsumingANumber` — out by one minor unit (₹0.0001); asserts no voucher, no line, no consumed number |
| C1.3 | Every create/alter/delete is logged with user + timestamp + before/after | **Met** | `AuditTrailCaptureTests.SaveChangesAsync_OnCreate_WritesAnAfterImageAndNoBeforeImage`, `…_OnUpdate_WritesBothImagesAndTheChangedProperties`; `AuditStampTests` (user + timestamp, blank user throws); the ledger case in `PostingWorkflowTests.EditLog_ListForEntity_ShowsBeforeAndAfterImagesOfALedgerChange`; and the account-**group** case §14 also names, **written this session** — `PostingWorkflowTests.EditLog_ForAnAccountGroup_ShowsBeforeAndAfterImagesOfARenameAndAMove` |
| C1.4 | The audit trail cannot be turned off | **Met — all five defences** | (a) `AuditLogEntryTests.AuditLogEntry_DoesNotDeriveFromEntity` + `NoBusinessLogicGuardTests.AuditLogEntry_IsNotAmongTheEntityDerivedTypes`; (b) `AuditTrailCaptureTests.SaveChangesAsync_WhenALogRowIsMarkedModified_ThrowsTamperException` and `…MarkedDeleted…`; (c) `AuditTrailCaptureTests.RawConnection_AlteringOrRemovingALogRow_IsAbortedByTheTrigger`; (d) + (e) **written this session** — `AuditTrailCannotBeDisabledGuardTests` |
| C1.5 | Trial balance totals to zero for ≥500 randomised vouchers | **Met** | **New** — `RandomisedLedgerFixtureTests.TrialBalance_OverFiveHundredRandomisedVouchers_TotalsToZeroExactly`, 523 vouchers; `SUM(AmountMinor) == 0` exactly, no tolerance. Statements in §3 |
| C1.6 | Assets == Liabilities + Equity + P&L for the same dataset | **Met** | **New** — `RandomisedLedgerFixtureTests.BalanceSheet_OverFiveHundredRandomisedVouchers_BalancesExactly`; the P&L figure is read from the balance-sheet query, not recomputed. **This criterion was failing until the defect in §6.1 was fixed** |
| C1.7 | Day book for a range matches the sum of its vouchers | **Met** | **New** — `RandomisedLedgerFixtureTests.DayBook_OverTheFixturePeriod_MatchesTheSumOfItsVouchers` (523 vouchers, debits == credits == the posted total); cancelled-and-flagged by `Cancel_PostedVoucher_KeepsItsNumberOccupiedAndFlagsTheDayBook` |
| C1.8 | Numbering resets each FY, blocks gaps, logs manual overrides | **Met** | `Post_AcrossAFinancialYearBoundary_RestartsTheCounter`; `Cancel_PostedVoucher_KeepsItsNumberOccupiedAndFlagsTheDayBook` (number not reissued, gap report empty); `Post_ManualNumberOverride_AdvancesHighWaterAndIsRecorded` (high-water advances, `IsManualNumber` persisted, duplicate override refused); `AccountingRoundTripTests.SaveChangesAsync_ForADuplicateCurrentVoucherNumber_IsRejectedByTheFilteredIndex` |
| C1.9 | The journal is append-only | **Met** | `Alter_PostedVoucher_WritesAppendOnlyHistory`; `AccountingRoundTripTests.SaveChangesAsync_ForASupersededRevision_FreesTheNumberForItsSuccessor` |
| C1.10 | A closed period rejects postings | **Met** | `Post_IntoLockedPeriod_IsRefusedButLaterDatesAreNot`; `Alter_CannotMoveAVoucherIntoALockedPeriod` |
| C1.11 | No inline SQL outside the dialect layer; every query key resolves | **Met** | Both halves **written this session**: `SqlStatementSourceTests.Dialect_ResolvesEveryKeyTheAccountingReportsDeclare` walks all 13 keys of `AccountingQueryKeys.All` against the registered dialect, and `Phase1ArchitectureGuardTests.NoSqlStatement_AppearsOutsideTheDialectLayer` scans both SQL-free projects. The scan matches upper-case `SELECT` only, so it cannot fire on `"Select a group…"` or `.Select(` — a guard that cries wolf gets deleted |
| C1.12 | No stored balance column exists | **Met** | `AccountingModelTests.Model_HasNoMappedPropertyNamedLikeAStoredBalance`; `AccountingMigrationTests.AddAccountingCore_CreatesNoColumnHoldingABalanceOrATotal`; and visible in the DDL at `phase-1-schema.sql` (§4) |
| C1.13 | Cost-centre schema ships with no UI | **Met** | Schema half: `AccountingMigrationTests.AddAccountingCore_ShipsCostCentreSchemaWithANullableJournalLineColumn`, `AccountingModelTests.Model_ShipsCostCentreSchemaWithANullableJournalLineForeignKey`, DDL §4.3. No-UI half **written this session**: `Phase1ArchitectureGuardTests.NoViewOrViewModel_ReferencesCostCentre` over every `.cs` and `.xaml` under Views and ViewModels |
| C1.14 | Materialised paths agree with `ParentGroupId` | **Met** | **New** — `MaterialisedPathTests` (6 tests). A 40-node randomised tree, then 25 attempted re-parentings, with every path recomputed from `ParentGroupId` alone after **each** attempt — refusals included, since a rejected move must leave the cache as consistent as an accepted one. This criterion had nothing to test until this session: see §6.4 |
| C1.15 | Money arithmetic is exact | **Met** | `MoneyScaleTests.ToMinor_RandomisedAmountsSummingToZero_MinorUnitSumIsExactlyZero`, the midpoint-rounding pair, C1.5's exact zero over 523 vouchers, and `AccountingModelTests.Model_MapsNoDecimalPropertyOntoATextAffinityColumn`. The no-`double` scan **written this session** — `Phase1ArchitectureGuardTests.NoAccountingOrReportingType_ExposesABinaryFloatingPointNumber`, by reflection so a comment reading "double-counts" cannot trip it, plus `TheDoublesThatDoExist_AreOnlyTheTwoNonMonetaryOnes`, which pins the only five to paper millimetres and a progress ratio |
| C1.16 | The audit hash chain verifies, and detects tampering | **Met** | Verification over the C1.5 fixture: `RandomisedLedgerFixtureTests.EditLogChain_OverFiveHundredRandomisedVouchers_VerifiesIntact`. The database-level tamper case **written this session** — `AuditChainTamperTests` drops both triggers (what an attacker with the file does), rewrites a row in the **middle** of the chain, and asserts verification names that row as the first break; a second test deletes a row and asserts the hole is evident |
| C1.17 | `SupersededByRevisionId` has no foreign key, deliberately | **Met** | `UniqueIndexNullSemanticsTests.Model_SupersededByRevisionId_HasNoForeignKey` |
| C1.18 | An alteration supersedes before it inserts | **Met** | **Written this session** — `PostingWorkflowTests.Alter_PostedVoucher_IssuesTheSupersedingUpdateBeforeTheSuccessorInsert`. A `SqlCommandRecorder` (`DbCommandInterceptor`) captures every statement one `AlterAsync` emits through the real DI composition; the test asserts the `UPDATE … SupersededByRevisionId` precedes the successor `INSERT`, and that both carry the same non-null transaction. The ordering was already correct — it simply had no evidence |
| C1.19 | Every voucher has exactly one current revision | **Met** | `RandomisedLedgerFixtureTests.EveryVoucher_OverTheFixture_HasExactlyOneCurrentRevision` over all 523, including that every non-null `SupersededByRevisionId` resolves to a revision of the *same* voucher. The rolled-back half **written this session** — `AuditChainTamperTests.EveryVoucher_AfterAnAlterationThatWasRefused_StillHasExactlyOneCurrentRevision`, with three refusals that fail at three different points of `AlterAsync` |
| C1.20 | No unique index depends on NULL semantics | **Met** | `UniqueIndexNullSemanticsTests.Model_EveryUniqueIndex_EitherHasNoNullableKeyColumnOrHasAFilter`; `AccountingMigrationTests.AddAccountingCore_MakesTheDeclaredUniqueIndexesUnique`, `…_CreatesTheCurrentRevisionNumberIndexAsAPartialUniqueIndex`; and the DDL excerpt in §4.2 |

**Summary: 20 met, 0 partially met, 0 unmet.**

---

## 2. Build and test run

### 2.1 Build

```
dotnet build VinayakaAgencies.sln -c Debug
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Warning-free across all eight projects, with nullable warnings-as-errors enforced.

### 2.2 Test run

```
dotnet test VinayakaAgencies.sln -c Debug
```

```
Passed!  - Failed: 0, Passed: 485, Skipped: 0, Total: 485, Duration:    523 ms - VinayakaAgencies.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 112, Skipped: 0, Total: 112, Duration:    205 ms - VinayakaAgencies.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 175, Skipped: 0, Total: 175, Duration:      5 s - VinayakaAgencies.App.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 278, Skipped: 0, Total: 278, Duration:     59 s - VinayakaAgencies.Infrastructure.Tests.dll (net8.0)
```

| Project | Passed | Failed | Total |
|---|---|---|---|
| VinayakaAgencies.Domain.Tests | 485 | 0 | 485 |
| VinayakaAgencies.Application.Tests | 112 | 0 | 112 |
| VinayakaAgencies.App.Tests | 175 | 0 | 175 |
| VinayakaAgencies.Infrastructure.Tests | 278 | 0 | 278 |
| **Total** | **1,050** | **0** | **1,050** |

This run covers the whole product through Phase 8, not Phase 1 alone — the suites are not
partitioned by phase. The twelve tests added this session are:

| Test | Criterion |
|---|---|
| `RandomisedLedgerFixtureTests.Fixture_PostsMoreThanFiveHundredVouchers` | C1.5's premise |
| `RandomisedLedgerFixtureTests.TrialBalance_OverFiveHundredRandomisedVouchers_TotalsToZeroExactly` | C1.5 |
| `RandomisedLedgerFixtureTests.BalanceSheet_OverFiveHundredRandomisedVouchers_BalancesExactly` | C1.6 |
| `RandomisedLedgerFixtureTests.DayBook_OverTheFixturePeriod_MatchesTheSumOfItsVouchers` | C1.7 |
| `RandomisedLedgerFixtureTests.EditLogChain_OverFiveHundredRandomisedVouchers_VerifiesIntact` | C1.16 |
| `RandomisedLedgerFixtureTests.EveryVoucher_OverTheFixture_HasExactlyOneCurrentRevision` | C1.19 |
| `PostingWorkflowTests.Post_ContraBetweenCashAndBank_IsAccepted` | regression, §6.2 |
| `PostingWorkflowTests.BalanceSheet_WithABalanceOnEachSide_ShowsLiabilitiesPositiveAndBalances` | regression, §6.1 |
| `PostingWorkflowTests.ListBankLedgers_ForALedgerUnderBankAccounts_ReturnsIt` | regression, §6.3 |
| `AuditTrailCannotBeDisabledGuardTests.AuditTypes_InApplicationAndApp_DeclareNoMemberThatCouldSwitchTheTrailOff` | C1.4(d) |
| `AuditTrailCannotBeDisabledGuardTests.Appsettings_OnDisk_HasNoKeyThatCouldSwitchTheTrailOff` | C1.4(e) |
| `SqlStatementSourceTests.Dialect_ResolvesEveryKeyTheAccountingReportsDeclare` | C1.11 |

---

## 3. The ≥500-voucher fixture and its statements

`RandomisedLedgerFixture` posts **523 vouchers** through `IVoucherPostingService` — the same path
the shell drives, one DI scope and one unit of work per post — against a real SQLite file. Spread:

- **Ledgers:** 15 postable, across five natures. Ten created by the fixture (a bank account, two
  debtors, two creditors, three expense ledgers of two groups, an income ledger, a fixed asset)
  plus five of the §4.5 seeded ledgers. `Profit & Loss A/c` is excluded because it is the one
  ledger barred from manual posting — its figure is derived (§8.4).
- **Voucher types:** Payment, Receipt, Journal, and Contra for one in nine (restricted to cash and
  bank per §8.3).
- **Dates:** every business date in FY 2026-27 is a candidate; the year is picked at random per
  voucher.
- **Amounts:** ₹1.00 to ₹99,999.99 at two decimal places. Each voucher is two to four lines, one
  side generated and the other a single balancing plug, so it balances by construction without any
  figure being chosen to make the arithmetic tidy.
- **Seed:** constant (`20261005`). "Randomised" in C1.5 means the figures are not hand-picked to
  balance; it does not mean irreproducible. The owner can re-run and get the statements below.

Regenerate with:

```powershell
$env:VA_EVIDENCE_DIR = "C:\temp\va-evidence"
dotnet test tests\VinayakaAgencies.Infrastructure.Tests --filter "FullyQualifiedName~RandomisedLedgerFixtureTests"
```

### 3.1 The three statements, as produced

```
Fixture: 523 randomised vouchers, seed 20261005, 01 Apr 2026 to 31 Mar 2027

TRIAL BALANCE as on 31 Mar 2027
Ledger                            Group                                Debit            Credit
Discount Allowed                  Indirect Expenses              6,82,239.55              0.00
Electricity Expenses              Indirect Expenses              1,86,647.43              0.00
Round Off                         Indirect Expenses                     0.00              0.00
Shop Rent                         Indirect Expenses              2,21,264.19              0.00
Delivery Van                      Fixed Assets                   3,49,761.89              0.00
Opening Balance Difference        Suspense A/c                          0.00              0.00
Commission Received               Indirect Incomes               1,16,909.98              0.00
Discount Received                 Indirect Incomes                      0.00      16,10,642.34
Karnataka Bank — Current A/c      Bank Accounts                         0.00       7,79,537.70
Ravi Motors                       Sundry Debtors                 3,75,699.31              0.00
Sri Lakshmi Traders               Sundry Debtors                        0.00       2,79,372.51
Stock-in-Hand                     Stock-in-Hand                         0.00              0.00
Cash                              Cash-in-Hand                          0.00       1,52,069.98
Profit & Loss A/c                 Capital Account                       0.00              0.00
Purchases                         Purchase Accounts              6,31,665.75              0.00
Freight Inward                    Direct Expenses                       0.00       4,38,106.04
Sales                             Sales Accounts                        0.00       3,12,688.39
Crompton Greaves Ltd              Sundry Creditors                      0.00       2,48,512.69
Kirloskar Brothers Ltd            Sundry Creditors              12,56,741.55              0.00
TOTAL                                                           38,20,929.65      38,20,929.65
Balanced: True

PROFIT & LOSS 01 Apr 2026 to 31 Mar 2027
Sales                             Income             3,12,688.39
Freight Inward                    Expense           -4,38,106.04
Purchases                         Expense            6,31,665.75
Commission Received               Income            -1,16,909.98
Discount Received                 Income            16,10,642.34
Discount Allowed                  Expense            6,82,239.55
Electricity Expenses              Expense            1,86,647.43
Shop Rent                         Expense            2,21,264.19
Total income 18,06,420.75  total expense 12,83,710.88  gross 1,19,128.68  net 5,22,709.87

BALANCE SHEET as on 31 Mar 2027
Assets
  Karnataka Bank — Current A/c          -7,79,537.70
  Cash                                  -1,52,069.98
  Delivery Van                           3,49,761.89
  Stock-in-Hand                                 0.00
  Ravi Motors                            3,75,699.31
  Sri Lakshmi Traders                   -2,79,372.51
  TOTAL ASSETS                          -4,85,518.99
Liabilities
  Profit & Loss A/c                      5,22,709.87
  Crompton Greaves Ltd                   2,48,512.69
  Kirloskar Brothers Ltd               -12,56,741.55
  Opening Balance Difference                    0.00
  TOTAL LIABILITIES                     -4,85,518.99
Balanced: True
```

**Reading these figures.** Trial-balance totals agree exactly at ₹38,20,929.65 (C1.5). The balance
sheet's two sides agree exactly at −₹4,85,518.99 (C1.6).

Both balance-sheet totals being *negative*, and several individual lines carrying a sign a real
business would not show, are properties of the fixture rather than defects: random postings in both
directions against every ledger overdraw the bank, put a credit balance on a debtor and a debit
balance on a creditor. What C1.5 and C1.6 assert is the **identity**, and the identity holds. A
side-effect worth noting is that the P&L shows negative figures within a nature for the same
reason — a credit posted to an expense ledger is legal and the fixture does it.

For the signs a real book produces, see
`PostingWorkflowTests.BalanceSheet_WithABalanceOnEachSide_ShowsLiabilitiesPositiveAndBalances`:
one credit purchase, creditor shown at +₹40,000.00 on the liabilities side, loss at +₹40,000.00 on
the assets side, both totals equal.

---

## 4. Schema

### 4.1 Migrations

```
dotnet ef migrations list -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App --no-connect
```

```
20260814023230_InitialCreate
20260815092544_AddAuditLogTable
20260815095651_AddAccountingCore
20260819081509_AddNameUniquenessIndexes
20260822054301_AddJournalLineVoucherId
20260822125403_AddInventoryCore
…
```

The first three are Phase 1's. Their full DDL is filed alongside this report as
**`specs/evidence/phase-1-schema.sql`** (360 lines), generated with:

```powershell
dotnet ef migrations script 0 AddAccountingCore -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App -o specs\evidence\phase-1-schema.sql
```

### 4.2 What the reviewer can check in that file

**The audit triggers exist** (C1.4, defence (c)):

```sql
CREATE TRIGGER TR_AuditLogEntries_BlockUpdate
BEFORE UPDATE ON AuditLogEntries
BEGIN
    SELECT RAISE(ABORT, 'The audit trail is append-only: audit log rows cannot be updated.');
END;

CREATE TRIGGER TR_AuditLogEntries_BlockDelete
BEFORE DELETE ON AuditLogEntries
BEGIN
    SELECT RAISE(ABORT, 'The audit trail is append-only: audit log rows cannot be deleted.');
END;
```

**Every unique index either has no nullable key column or carries a filter** (C1.20, §5). The two
with nullable key columns are both filtered:

```sql
CREATE UNIQUE INDEX "IX_Companies_Gstin" ON "Companies" ("Gstin") WHERE [Gstin] IS NOT NULL;
CREATE UNIQUE INDEX "IX_VoucherRevisions_CompanyId_VoucherTypeId_SeriesId_FinancialYearId_NumberNormalised"
    ON "VoucherRevisions" (…, "NumberNormalised") WHERE [SupersededByRevisionId] IS NULL;
```

**No stored balance column** (C1.12). A grep of the DDL for a column whose name contains `Balance`,
`ClosingBal` or `Total` returns nothing.

### 4.3 Cost centres: schema, no UI (C1.13)

```sql
CREATE TABLE "CostCategories" ( … );
CREATE TABLE "CostCentres" ( … "ParentCostCentreId" TEXT NULL, … );
-- on JournalLines:
    "CostCentreId" TEXT NULL,
    CONSTRAINT "FK_JournalLines_CostCentres_CostCentreId"
        FOREIGN KEY ("CostCentreId") REFERENCES "CostCentres" ("Id") ON DELETE RESTRICT,
```

Nullable, restrict-on-delete, indexed. The no-UI half of the criterion is unevidenced — see §1.

---

## 5. Artefacts §15 asks for that are not in this file

§15 lists seven artefact classes. Four are above. These three are not, and each needs something a
test run cannot produce:

1. **Screenshots** of the ledger master, voucher entry, day book, trial balance and edit-log viewer
   — `NOT CAPTURED`. They need the app running interactively, which is the owner walkthrough.
2. **The edit-log extract for one voucher taken through create → alter → cancel, showing the
   before/after images.** The workflow is asserted
   (`Alter_PostedVoucher_WritesAppendOnlyHistory`, `Cancel_PostedVoucher_…`,
   `EditLogChain_AfterAFullWorkflow_VerifiesIntact`) and `EditLog_ListForEntity_…` asserts the
   images for a ledger, but no run transcribes the JSON pair for a voucher into a reviewable
   extract. Worth adding to the same evidence-dump hook the fixture uses.
3. **The numbering-gap report for a series where a cancellation *and* a manual override both
   occurred.** The cancellation half is asserted — the report is empty, because a cancelled
   voucher's number stays taken (`Cancel_PostedVoucher_KeepsItsNumberOccupiedAndFlagsTheDayBook`).
   The manual-override half is not: overriding to `PMT/0010` after `PMT/0003` leaves 0004–0009
   genuinely missing, and that the report surfaces them is the behaviour open-questions.md #11
   promises. **Untested.**

---

## 6. Defects found while producing this evidence

All three are pre-existing Phase 1 defects, found because this was the first time the accounting
core was exercised at volume and with a balance on both sides of the sheet. All three are fixed,
each with a named regression test.

### 6.1 The balance sheet did not balance (C1.6)

`GetBalanceSheetAsync` negated the sign of every liability row **twice** — once in SQL:

```sql
CASE WHEN g.Nature = 'Liability' THEN -COALESCE(p.BalanceMinor, 0) ELSE … END
```

and again in `AccountingReportService.MapBalanceSheetRow`:

```csharp
var amount = nature == AccountNature.Liability ? -row.AmountMinor : row.AmountMinor;
```

Each site carried a comment explaining that *it* was the one normalising the sign, so whoever wrote
the second did not see the first. The two cancelled. Consequences:

- **Every liability line on the balance sheet showed with the wrong sign** — a creditor owed
  ₹40,000 displayed as −40,000.00.
- **`IsBalanced` was false for any real company.** It compares `TotalAssets == TotalLiabilities`,
  which with both sides left at their raw signed sums can only be true when both are zero.

Why no test caught it: the only balance-sheet assertions were in
`Post_BalancedPayment_UpdatesEveryDerivedStatement`, whose single payment leaves **both sides at
zero** — the one case where the broken comparison passes. A second, related defect hid in the same
query: the derived `Profit & Loss A/c` ledger was selected by the ledger half of the `UNION` *and*
injected by the second half, so on a company in profit the sheet listed it twice, once at 0.00.

**Fix.** The query now emits raw signed minor units and no presentation logic; the mapper is the
single place the sign is normalised, so the Phase 9 SQL Server dialect cannot disagree with the
SQLite one about the sign of a creditor. The derived ledger is excluded from the ledger half via
`AllowsManualPosting = 1`. Regression:
`PostingWorkflowTests.BalanceSheet_WithABalanceOnEachSide_ShowsLiabilitiesPositiveAndBalances`,
plus C1.6 itself.

**Impact had it shipped:** the balance sheet — a §2 deliverable — was wrong on every install with
a creditor or a profit, in both the figures and the balanced/not-balanced verdict.

### 6.2 Every Contra voucher was refused

`VoucherPostingService.CashAndBankGroupNames` held display-cased literals
(`"Cash-in-Hand"`, `"Bank Accounts"`, `"Bank OD A/c"`) and matched them against
`AccountGroup.NameNormalised` — which `NameNormaliser` **upper-cases** — using a case-sensitive
`StringComparer.Ordinal`. Nothing ever matched, so the set of cash-and-bank paths was always empty
and the rule rejected **every** Contra voucher, including a plain cash-to-bank deposit.

Why no test caught it: the only Contra test was `Post_ContraToNonCashLedger_IsRefused`, a negative.
A rule that refuses everything also refuses what it should, so it passed throughout.

**Fix.** The set is built through `NameNormaliser.Normalise` and compared with a `HashSet`.
Regression: `PostingWorkflowTests.Post_ContraBetweenCashAndBank_IsAccepted`.

**Impact had it shipped:** one of the eight voucher types was entirely unusable — no cash deposit
into the bank and no withdrawal could be recorded.

### 6.3 The bank-reconciliation screen could never list an account

`ChartOfAccountsService.ListBankLedgersAsync` carried the identical mistake with
`BankRootGroupNames`, so it always returned an empty list. That list is what
`BankReconciliationViewModel` loads into its account picker, so manual bank reconciliation — also a
§2 deliverable — had nothing to reconcile against on any install. It had no test at all.

**Fix.** Same change. Regression:
`PostingWorkflowTests.ListBankLedgers_ForALedgerUnderBankAccounts_ReturnsIt`.

### 6.4 An account group could not be re-parented at all

C1.14 asks for a randomised group tree with nodes **re-parented**, and could not be written,
because no application service re-parented a group. `AccountGroup.MoveTo` existed and was unit
tested; it changes `ParentGroupId` and leaves `MaterialisedPath` alone, carrying the comment *"The
caller is responsible for rebuilding the subtree's paths in the same transaction."*
`SetMaterialisedPath` carries the matching warning. **There was no caller.** The obligation was a
pair of comments addressed to nobody.

That is not harmless. The path is what every balance roll-up range-scans, so the first caller to
appear — a later phase adding a "move group" screen, say — would have inherited a trap that
corrupts the roll-up of a whole subtree with no error anywhere.

**Fix.** `IChartOfAccountsService.MoveGroupAsync` re-parents and rebuilds the moved node and every
descendant's path, depth-first, in one unit of work. It refuses a destination inside the subtree (a
detached subtree is the one failure a path rebuild cannot recover from), a system group, a sibling
name clash, a move past the depth limit, and — deliberately — a move **across natures**, which
would shift balances between the P&L and the balance sheet without a voucher being touched. No
screen calls it; Phase 1 specifies no re-parent UI, and this is not the batch to invent one.

### 6.5 The Ledgers screen could never show a ledger

Found by running the product, not by reading it. The Ledgers master screen opened against a
database holding 26 ledgers and displayed *"No ledger matches this filter. Clear the search, or
create one below."* — with no search text and no group filter set.

`LedgersViewModel.RefreshAsync` is correct, and `ReloadLedgersAsync` does call `ApplyFilters()`.
The defect is that **nothing could invoke it**: no binding in `LedgersView.xaml`, no entry in the
screen's shortcut bars, and no internal caller. Zero references. The list was unreachable, and the
empty-state message blamed a filter the operator had not set.

Thirty-two of the forty-seven views bind a load command; this one did not.

**Fix.** A *Refresh* control in the filter bar, matching `AccountGroupsView`. Two regressions:
`ScreenLoadReachabilityTests` asserts every screen with a load command has *some* path to it — a
XAML binding, a shortcut-bar button or an internal caller — and
`EveryScreenAgainstARealDatabaseTests.LedgersScreen_AfterRefresh_PopulatesTheCollectionTheGridBinds`
asserts `VisibleLedgers` fills, not merely `Ledgers`.

**Why no test caught it, including the new ones.** The screen sweep in §8 finds a load command by
reflection and invokes it directly. That proves the command works while saying nothing about
whether the product can call it — testing through a door the application does not have. The
reachability guard exists because of exactly that blind spot.

**Impact had it shipped:** one of the ten Phase 1 §2 screens was inoperable, and the message shown
would have sent the owner looking for a filter to clear.

### 6.6 Note on the shape of these

Two of the first three are the same bug written twice: a hardcoded display-cased name compared
ordinally against a normalised column. A sweep of every other `NameNormalised` comparison in `src/`
found no third instance — the remaining uses either normalise the comparand first or are `OrderBy`
clauses, where casing changes the order and nothing else.

§6.4 and §6.5 share a different shape, and it is the one worth carrying forward: **code that is
correct and unreachable.** A domain method whose contract nothing honours; a command nothing binds.
Unit tests reach both directly and pass. Only assembling the real thing — the real container, the
real database, or in §6.5's case the real application — asks whether anything actually calls them.

---

## 7. Outstanding before Phase 1 sign-off

1. **Owner review of the seeded ledgers and numbering prefixes** (§16, open-questions.md #14/#15).
   The first financial year's numbers end up on printed documents, so this wants confirming before
   volume. `CTR/`, `PMT/`, `RCT/` and the rest are in use in the tests above.
2. **Screenshots** of the five screens §15 names, from the owner walkthrough.
3. **open-questions.md #17** — account-group name uniqueness, sibling-scoped today. Still `Open`,
   and tightening to company-wide later may be impossible once the chart of accounts is populated.
4. **The two evidence artefacts in §5** — the voucher edit-log extract and the numbering-gap report
   after a manual override.
5. **A decision on whether screens should load when opened.** The shell constructs a screen and
   shows it, and never invokes a load; every list screen therefore starts empty until the operator
   presses something. That is the documented flow for reports ("pick a date, *Load*") and is
   defensible for a day book spanning years, but it is why §6.5 went unnoticed for so long, and
   `chart-of-accounts-masters.md` already describes the Ledgers list as though it were populated on
   open. This is a UX decision across all 41 screens and so is the owner's, not one to settle in a
   batch about evidence.

---

## 8. Every screen, against a real database

Added this session, and the thing that found §6.5. `EveryScreenAgainstARealDatabaseTests` builds
the application's own composition (`AddApplication` + `AddInfrastructure`, nothing stubbed) over a
real migrated SQLite file with one provisioned company, then for all **41 registered screens**:

- resolves the view model from that container — proving the dependency graph the shell actually
  uses is complete, which no view-model unit test can show;
- runs every load/refresh command it exposes and asserts none faults;
- asserts the screen's own status line does not come back reading like a failure, since a screen
  that swallows its error into a status string is just as broken as one that throws;
- asserts the screens fed by a seed (Ledgers, Account groups, Godowns, Customer categories) come
  back with rows, with a companion test that each of those names still matches a registered screen
  so the list cannot rot into vacuity.

All 41 pass. The product was also launched and driven: it starts clean ("Shell window opened.", no
errors in the Serilog file), the Gateway renders with its accelerator letters, and Alt+G navigation
works. Screenshots are in `specs/evidence/images/` — `app-shell-gateway.png`,
`app-trial-balance.png`, `app-voucher-entry.png`, `app-day-book.png`, and the §6.5 pair
`app-ledgers-defect-no-refresh-control.png` / `app-ledgers-refresh-control-added.png`.

**One honest limit on those screenshots:** synthetic mouse clicks could not be delivered to the
window in this session, so the second Ledgers image shows the *Refresh* control present but the
grid still empty — it is evidence that the control now exists, not that clicking it fills the grid.
That it fills the grid is proven by
`LedgersScreen_AfterRefresh_PopulatesTheCollectionTheGridBinds` instead. The owner walkthrough is
still the step that confirms it on screen.
