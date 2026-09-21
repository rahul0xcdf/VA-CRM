# Phase 7 evidence — TDS/TCS, Payroll, Banking

**Spec:** `specs/phase-7-tds-payroll-banking.md` (Approved 18 Sep 2026)
**Batches:** 7a TDS/TCS + banking, 7b payroll (§4a D5)
**Filed:** 21 September 2026

---

## 1. Checkpoint results

| # | Criterion | Status | Evidence |
|---|---|---|---|
| C7.1 | A TDS-applicable purchase voucher correctly computes and posts the deduction using the config table, not a hardcoded rate | **Met** | `TdsWorkflowTests.PostAsync_DeductsUsingTheConfigTableAndPostsABalancedVoucher` |
| C7.2 | Adding a new section as a data row — with no code change and no rebuild — makes it usable | **Met** | `TdsWorkflowTests.AddingASectionAsDataOnly_MakesItUsableOnTheNextVoucher` |
| C7.3 | Cumulative threshold: the deduction starts at the transaction that crosses the annual limit, on the correct base | **Met** | `TdsWorkflowTests.TheTransactionThatCrossesTheAnnualLimit_DeductsOnTheCaughtUpBase` |
| C7.4 | A dated rate change applies correctly to vouchers on either side of the effective date | **Met** | `TdsWorkflowTests.ADatedRateChange_AppliesOnEachSideOfItsEffectiveDate` |
| C7.5 | A grep of `src/` finds no hardcoded section number or rate | **Met** | `NoHardcodedTaxFigureGuardTests` (6 tests) |
| C7.6 | 194Q and 206C(1H) are mutually exclusive on one transaction | **Met** | `TdsWorkflowTests.WhereTheBuyerDeclaresTheyDeduct_CollectionDoesNotAlsoApply` |
| C7.7 | With every payroll statutory flag off, no PF/ESI/PT row is produced and no statutory code path runs | **Met** | `PayrollWorkflowTests.WithEveryStatutoryFlagOff_NoStatutoryLineIsProducedAtAll` |
| C7.8 | The owner repositions a cheque layout and prints to a real cheque without a rebuild | **Partially met — software side proven, physical print pending owner** | `BankingWorkflowTests.RepositioningAChequeLayout_AppendsAVersionAndTakesEffectImmediately` |

### On C7.8

The half that is software is proven: the seeded CTS-2010 layout resolves as the default for the
`Cheque` kind, every field the renderer understands has a measured position in millimetres, editing
those positions appends a template version and takes effect on the next print, and version 1 stays
readable. No rebuild is involved at any point — that is the substance of the checkpoint.

What cannot be proven from a test is the last clause: **printing to a real cheque**. That needs a
blank leaf, a printer and the owner. The loop the checkpoint describes — print, measure, adjust,
print again — is built and exercised; whether the seeded millimetres land on this bank's leaf first
time is unknown, and the expectation in `ChequeLayoutDefaults` is explicitly that they will need
adjusting. This is filed as met-on-the-software-side and open for the owner walkthrough.

---

## 2. Test run

```
dotnet test VinayakaAgencies.sln
```

```
Passed!  - Failed: 0, Passed: 485, Skipped: 0, Total: 485 - VinayakaAgencies.Domain.Tests.dll
Passed!  - Failed: 0, Passed:  72, Skipped: 0, Total:  72 - VinayakaAgencies.Application.Tests.dll
Passed!  - Failed: 0, Passed: 133, Skipped: 0, Total: 133 - VinayakaAgencies.App.Tests.dll
Passed!  - Failed: 0, Passed: 229, Skipped: 0, Total: 229 - VinayakaAgencies.Infrastructure.Tests.dll
```

**919 tests, 0 failures.** Phase 7 added 96 of them:

| Suite | Added | Covers |
|---|---|---|
| `TdsMathTests` | 17 | Both threshold tests, the annual catch-up, a whole year taxed once, the no-PAN rate, four certificate cases, rupee rounding, overflow |
| `IndianAmountInWordsTests` | 32 | Indian grouping to crore and beyond, paise, the rounding that carries into the rupee, 3-then-2 digit grouping |
| `PayrollMathTests` | 19 | Both ceilings and their order, the slab lookup with boundaries, the three non-statutory bases, whole-rupee rounding |
| `NoHardcodedTaxFigureGuardTests` | 6 | C7.5, executed |
| `TdsWorkflowTests` | 13 | C7.1–C7.4, C7.6, certificates, challan |
| `BankingWorkflowTests` | 14 | Settlement posting, the four electronic modes, C7.8, cheque data, register |
| `PayrollWorkflowTests` | 10 | C7.7, the salary voucher, dated rates, leavers, challan |
| `GstRateScheduleTests` | 3 | Regression for the Phase 5 defect found during this phase (§5 below) |
| `Phase7ScreenShortcutTests` | 12 | Screen registration, ADR-0007 key sets, mode-dependent fields |

Every one of the seven new screens is also covered by the existing
`ScreenViewLoadTests.EveryRegisteredScreenView_LoadsWithTheApplicationResources` sweep, which
constructs each registered view against the application's real merged dictionaries — so a view
referencing a missing `StaticResource` fails the build rather than the operator.

---

## 3. What was built

**7a — TDS/TCS and banking**

- Domain: `TaxSection`, `TaxRateSchedule`, `TaxPayeeProfile`, `LowerDeductionCertificate`,
  `TaxDeduction`, `TdsSettings`, `TdsMath`; `PaymentMode`, `PaymentInstrument`,
  `IndianAmountInWords`.
- Application: `ITaxSectionService`, `ITaxDeductionService`, `ITdsSettingsService`,
  `IBankingService`, `ChequeLayoutDefaults`.
- Migration `AddTdsAndBanking` — 7 tables, no alterations to existing ones.
- Screens: TDS/TCS sections, TDS/TCS setup, TDS/TCS register, Payments & receipts, plus the cheque
  preview window.

**7b — payroll**

- Domain: `Employee`, `PayHead`, `SalaryStructureLine`, `StatutoryRateSchedule`,
  `ProfessionalTaxSlab`, `PayrollSettings`, `SalaryVoucher`, `SalaryVoucherLine`, `PayrollMath`.
- Application: `IPayrollMasterService`, `IPayrollRunService`.
- Migration `AddPayroll` — 8 tables.
- Screens: Employees, Payroll setup, Payroll run.

**Docs:** `docs/features/tds-tcs-engine.md`, `banking-and-cheque-printing.md`,
`payroll-statutory-basics.md`; `docs/user-guide/phase-7-tds-payroll-and-banking.md`.

---

## 4. Schema and contract changes beyond the spec

Recorded here as the working agreement requires. Both are additive and neither needed a data
migration.

### 4.1 `PrintTemplate.DocumentKind` retyped to `PrintDocumentKind`

§4a D4 rules that cheque layouts reuse the Phase 3 print-template engine rather than a second
template system. That engine typed its kind as `TradeDocumentKind` — an enum whose own
documentation reads "the seven documents of the sales and purchase pipelines". A cheque is a bank
instrument, not a trade document, and widening that enum would have put one inside it and grown a
dead arm on every pipeline `switch`.

A new `PrintDocumentKind` carries the seven trade members under identical names and numbers, plus
`Cheque`. The column stores enum *names*, so every existing template row reads back unchanged and
the migration diff for this change is empty. `TradeDocumentPrinting.ToPrintKind()` is the seam, and
it is an explicit switch rather than a cast so the two enums are free to diverge.

### 4.2 `PrintTemplateLayout.Fields` added

Cheque layouts need absolute field positions; invoices do not. Added as a nullable trailing
parameter with a `PlacedFields` projection that normalises null to empty — nullable because
templates written before Phase 7 have no such property in their stored JSON, and normalising here
rather than migrating the JSON keeps every existing revision readable exactly as it was saved.

### 4.3 `TaxPayeeProfile.DeclaresDeductingOnPurchases` added

Not in the spec's field list, but required by C7.6. The seller cannot see the buyer's books; the
only thing that can switch off collection is the buyer's written declaration, so the declaration is
what is recorded. Default false, which collects — the safe default of the two, because
over-collecting is refundable through the buyer's return while under-collecting is the seller's
liability with interest.

---

## 5. Defects found and fixed outside Phase 7 scope

Two pre-existing defects surfaced while building this phase. Both are reported here rather than
left in place.

### 5.1 A GST rate could never be changed (Phase 5)

`GstRateService.AddAsync` tested `Covers(effectiveFrom)` against the open-ended row it was meant to
supersede. An open-ended row covers *every* date after its start by definition, so the check
rejected every rate change and left the close-the-previous-row branch behind it unreachable. No
Phase 5 test added a second rate row for one code, so nothing caught it.

It surfaced because the Phase 7 TDS rate service was modelled on the same method and inherited the
same defect — which its own C7.4 test caught immediately. Both are fixed, and
`GstRateScheduleTests` is the regression that keeps the GST half fixed. The shared rule is written
once in `PayrollMasterService.CloseSupersededAsync` for this phase's five dated tables so they
cannot each rediscover it.

**Impact if it had shipped:** the owner could not have recorded a notified GST rate change at all.

### 5.2 GST seeding was never called at startup (Phase 5)

`EnsureGstSeedAsync` was written in Phase 5 and wired into nothing. An install that had never
opened a GST screen had no output/input tax ledgers for a tax line to post to. Now called from both
`App.OnStartup` and the first-run wizard, alongside the new TDS and payroll seeds.

---

## 6. Outstanding before sign-off

1. **Owner walkthrough of the seven new screens** and screenshots for this file.
2. **C7.8's physical half** — print a cheque onto a real leaf, adjust the millimetres, print again.
   The seeded CTS-2010 positions are a starting point and are expected to need adjusting.
3. **Headcount** (§5 of the spec, "one fact still wanted"). It gates nothing that is built — D3 was
   designed to be correct either way — and decides only whether the PF and ESI flags are switched
   on at go-live, which is a settings change.
4. **194J and 194I** (§4a D1) — worth confirming with the business's CA before the first quarter
   closes. Adding either is two data rows and no code, which is the point of C7.2; the omission is
   recorded as a decision rather than an oversight.
