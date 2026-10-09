# Phase 5 Evidence Report — GST Returns & Tax Invoice Compliance

```yaml
Phase: 5
Verified by: development agent, owner review pending
Verified on: 2026-10-05
Repo state verified: branch phase-7-tds-payroll-banking, at c37e563 plus this session's commits
Spec: specs/phase-5-gst-returns.md (Approved 23 Aug 2026, decisions in §4a)
```

Phase 5 was implemented and verified by tests in August 2026; this file is the evidence report that
was never filed. **Four of the five criteria are met. C5.1 is partially met** — its wording asks for
validation against the authoritative GSTN schema, and that schema is not obtainable from here. §1
explains what was done instead.

Pursuing C5.1 was worth it: reviewing the export against two independent published references to the
GSTN payload found **three real encoding defects**, one of which would have filed every cess amount
as nil. Writing the GSTR-9 test this file previously recorded as missing found **a fourth**. All four
are fixed, with regressions; they are in §5. Four further divergences are known-outstanding and
deliberately not guessed at — they need the schema and the owner's accountant, and are tracked as
**open-questions.md #23**.

---

## 1. Checkpoint results

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| C5.1 | A generated GSTR-1 JSON validates against the current GSTN schema for a test dataset | **Partially met — see below** | `GstReturnWorkflowTests.ExportGstr1_StampsPinnedSchemaVersion_AndIsSelfConsistent`; schema validation of a real export by a JSON Schema evaluator in `…ExportGstr1_FromPostedInvoices_ValidatesAgainstTheAsBuiltSchema`, with the schema's own behaviour proved by `Gstr1SchemaValidationTests` (5 tests) |
| C5.2 | Intra-state and inter-state invoices produce the correct CGST/SGST vs IGST split | **Met** | `GstComputationServiceTests.ComputeAndApply_IntraStateSupply_SplitsHalfCgstHalfSgst` and `…_InterStateSupply_ChargesFullIgst`; the snapshot written onto the aggregate at posting time (`…_SnapshotIsWrittenOntoTheAggregate`) and the refusal to silently recompute it (`…_Recomputation_IsRefusedNotOverwritten`); rupee round-off routed to `Gst.RoundOffLedger` (`…_PaiseTotal_RoutesRoundOffToDesignatedLedger`); reverse charge not billed to the party (`…_ReverseChargeSupply_DoesNotBillParty`); an unconfigured item computing zero *with a warning* rather than guessing (`…_UnconfiguredItem_ComputesZeroWithWarning`) |
| C5.3 | GSTR-3B summary figures reconcile to the same period's GSTR-1 and the books | **Met** | `GstReturnWorkflowTests.BuildGstr3B_MatchesGstr1MatchesPostedSnapshots` — all three compared in one test, which is the only way the criterion means anything: two of the three agreeing proves nothing about the third |
| C5.4 | GSTR-2B reconciliation classifies matched / missing-in-books / missing-in-portal | **Met** | `GstReturnWorkflowTests.ReconcileGstr2B_ClassifiesMatchedMissingInBooksAndMissingInPortal` — all three buckets in one fixture |
| C5.5 | A rate change with an effective date applies to invoices after that date and not before | **Met** | `GstRateServiceTests.Resolve_RateChangedEffectiveFirstJuly_JuneUsesTwelveAndJulyUsesEighteen` — asserted on both sides of the date; supersession (`Resolve_AddingNewerRow_ClosesOlderOpenEndedRow`), the no-schedule case returning zero rather than a guess (`Resolve_DateBeforeAnySchedule_ReturnsZeroRate`), overlap refused (`Add_OverlappingSchedule_IsRefused`), unknown code refused (`Add_UnknownHsnSacId_IsRefused`); effective-dating in the domain via `GstRateScheduleTests` |

### Why C5.1 is only partially met

What the test proves is substantial, and it is not a structural self-check in the weak sense:

- the export is stamped with the schema version it was written for (`version` = `v3.2.4`, pinned in
  `GstSchemaVersions.CurrentGstr1`) and with a generation timestamp, so a portal rejecting an old
  format can be traced to the build that wrote it;
- `gstin` and the filing period `fp` (`202627`) are correct;
- the grand totals equal the books — ₹1,500 taxable, ₹180 IGST, ₹45 CGST, ₹45 SGST;
- B2B carries one buyer with one invoice (`inum` = `INV/0001`, `rchrg` = `N`) and its rate line at
  18%; B2C is aggregated by place of supply and rate;
- Table 12 (`hsn.data`) has both supply types, and documents-issued accounts for both invoices
  (`tot` 2, `cancel` 0, `net` 2).

What it does **not** do is the thing the criterion literally asks: validate against the GSTN schema.
**There is no GSTN schema artefact in the repository.** It is published behind the authenticated
GSTN developer portal (`developer.gst.gov.in/apiportal/taxpayer/returns` → *GSTR1 – Save GSTR1
data* → *Request Payload* → *Download Schema*), so only the owner can fetch it. That is now tracked
as **open-questions.md #23**, the same shape of blocked external dependency as the NIC credentials
(#10) and the real Tally export (#22).

**What was added instead**, following the Phase 6 precedent of building everything above a missing
external artefact and verifying it against a recorded contract:

- The emitted shape is pinned in
  `tests/VinayakaAgencies.Infrastructure.Tests/Gst/Schemas/gstr1-as-built.schema.json` and a real
  export from posted invoices is evaluated against it by a JSON Schema evaluator (`JsonSchema.Net`,
  MIT). The schema is `additionalProperties: false` throughout, so a field renamed, retyped,
  dropped or added in `Gstr1JsonExporter` fails a test rather than the portal.
- The schema is itself tested, so the validation cannot quietly become theatre:
  `Gstr1SchemaValidationTests` proves an empty object fails, an unknown field fails, and each of
  the two shapes §5.3 found wrong fails. A schema that failed to load, or that asserted nothing,
  would be caught by these rather than passing everything.
- The file's own `description` marks every field **CONFIRMED** or **UNVERIFIED**, so the next
  reader can see exactly how far the contract has been pinned down.

**This is deliberately not called proof of C5.1.** It validates our output against our own
understanding; the circularity is real and is the reason the criterion names GSTN's schema. What it
does buy is that the understanding is now written down, externally cross-checked where it could be,
and locked against drift — and the review that produced it found four real defects (§5).

Closing C5.1 needs the schema dropped into that folder as `gstr1-gstn.schema.json` (the csproj globs
the folder, so no project edit) plus its own test, and ideally a dry run of one real export through
the GSTN offline utility — the only step that proves the utility accepts the file.

---

## 2. Build and test run

```
dotnet build VinayakaAgencies.sln -c Debug
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
dotnet test VinayakaAgencies.sln -c Debug
```

```
Passed!  - Failed: 0, Passed: 485, Skipped: 0, Total: 485, Duration:    523 ms - VinayakaAgencies.Domain.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 112, Skipped: 0, Total: 112, Duration:    205 ms - VinayakaAgencies.Application.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 175, Skipped: 0, Total: 175, Duration:      5 s - VinayakaAgencies.App.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 278, Skipped: 0, Total: 278, Duration:     59 s - VinayakaAgencies.Infrastructure.Tests.dll (net8.0)
```

**1,050 tests, 0 failures.** The run covers the whole product through Phase 8 — the suites are not
partitioned by phase, so this is the state of Phase 5's tests today rather than as of the August
batch.

The Phase 5 tests inside it, by file:

| Suite | Covers |
|---|---|
| `GstTaxMathTests` (Domain) | The split arithmetic and its rounding |
| `HsnSacTests` (Domain) | The HSN/SAC master and its validation |
| `GstFinancialYearTests` (Domain) | Filing-period derivation on the Indian calendar |
| `GstRateScheduleTests` (Domain) | Effective-dated rate rows and which one covers a date |
| `GstComputationServiceTests` (Application) | C5.2 and the five posting-time behaviours listed in §1 |
| `GstRateServiceTests` (Application) | C5.5 and the supersession/overlap/unknown-code rules |
| `GstReturnWorkflowTests` (Infrastructure) | C5.1, C5.3, C5.4 and GSTR-9 end to end against a real SQLite file |
| `GstRateScheduleTests` (Infrastructure) | The regression for the §5.1 defect below |
| `Gstr1SchemaValidationTests` (Infrastructure) | **New** — the as-built schema's own behaviour, including that it rejects both shapes §5.3 found wrong |

---

## 3. Decisions as built (spec §4a, open-questions.md #8 and #21)

1. **Single GSTIN, one state, regular scheme.** The schema stays multi-company-ready; only
   single-registration flows are built.
2. **Reverse-charge paths built though rare** — `ComputeAndApply_ReverseChargeSupply_DoesNotBillParty`.
3. **Domestic B2B + B2C only.** No exports, SEZ or e-commerce sections.
4. **GSTN schema pinned at `v3.2.4`** and stamped on every export — the subject of §1 above.
5. **No rates or codes seeded anywhere.** Owner data only, per non-negotiable #6. This is why
   `ComputeAndApply_UnconfiguredItem_ComputesZeroWithWarning` matters: with nothing seeded, the
   unconfigured case is the *first* case a new install hits, and it must warn rather than invent a
   rate.

The same batch delivered the ADR-0005 presentation foundation (shared theme dictionaries, shell
rework, Go To palette, `IScreenShortcuts`). That work was **superseded by ADR-0006 and ADR-0007**
in September 2026 — the owner judged the ADR-0005 sidebar unusable and the shell was rebuilt. The
keyboard map now lives in `Presentation/Keyboard/AppShortcut.cs` with a duplicate-gesture test, and
`docs/user-guide/keyboard-shortcuts.md` is the current reference. Anything in the Phase 5 spec
describing the ADR-0005 shell should be read as history.

---

## 4. GSTR-9: was shipped untested, now covered

`IGstReturnService.BuildGstr9Async` shipped with **no test referencing it** — a statutory annual
return the owner files, built from twelve monthly rows, with nothing asserting those rows summed to
the year or agreed with the same months' GSTR-1s. It is not one of C5.1–C5.5, so no criterion failed
on it. Two tests now cover it, modelled on C5.3's three-way reconcile:

| Test | What it asserts |
|---|---|
| `BuildGstr9_OverAFinancialYear_ReconcilesToItsMonthlyGstr1sAndTheBooks` | Twelve rows for FY 2026-27, labelled April→March; invoices in the **first**, a middle and the **last** month so an off-by-one at either end cannot hide; each lands in its own month and nowhere else; the annual total equals the sum of its own rows, **and** the sum of twelve independently built monthly GSTR-1s, **and** the snapshots stamped on the posted documents |
| `BuildGstr9_ForAPeriodNotStartingOnTheFirstOfAMonth_LosesNoInvoice` | The regression for §5.4 below |

The three-way comparison is the point. Checking the annual total against the sum of its own monthly
rows alone would have passed while the defect below was live, because the defect dropped the invoice
from the rows *and* from the total derived from them — they agreed with each other and both were
wrong.

---

## 5. Defects found and fixed after this phase

Both were found during Phase 7 and fixed there (recorded in `phase-7-evidence.md` §5). They are
repeated here because the code is Phase 5's and this is Phase 5's evidence file.

### 5.1 A GST rate could never be changed

`GstRateService.AddAsync` tested `Covers(effectiveFrom)` against the open-ended row it was meant to
supersede. An open-ended row covers *every* date after its start by definition, so the guard
rejected every rate change and the close-the-previous-row branch behind it was unreachable. No
Phase 5 test added a second rate row for one code, so nothing caught it.

It surfaced because Phase 7's TDS rate service was modelled on the same method and inherited the
same defect, which that phase's C7.4 caught at once. `GstRateScheduleTests` (Infrastructure) is the
regression that keeps the GST half fixed.

**Impact had it shipped:** the owner could not have recorded a notified GST rate change at all —
the one thing effective-dated rate schedules exist for.

### 5.2 GST seeding was never called at startup

`EnsureGstSeedAsync` was written in Phase 5 and wired into nothing. An install that had never
opened a GST screen had no output or input tax ledgers for a tax line to post to. It is now called
from both `App.OnStartup` and the first-run wizard.

**Impact had it shipped:** the first GST invoice on a fresh install would have failed to post.

### 5.3 Three GSTR-1 export fields did not match the GSTN contract

Found by reviewing the export against two independent published references to the GSTN GSTR-1
payload while pursuing C5.1. All three are encoding — the figures were right, the keys and nesting
were not — so none would have been visible in the books, only at the portal.

| Field | Was | Is | Consequence if filed |
|---|---|---|---|
| Cess amount, in **all four** places it appears | `csft` | `csamt` | **Every cess amount would have filed as nil.** `csft` is not a GSTN field, so the portal would not have read it — and a return that under-reports cess is the filer's liability |
| HSN code in the Table 12 rows | `hsn` | `hsn_sc` | Table 12 rows with no recognisable HSN |
| A B2B invoice's rate figures | flat on each `itms` entry | nested under `num` + `itm_det` | The portal nests the figures one level down; a flat object is not the shape it accepts, so the items of every B2B invoice would have been rejected or misread |

Why nothing caught them: the only export test asserted the fields it already believed in — it read
`itms[0].rt` and never looked at cess at all. A test written from the same understanding as the code
cannot find a disagreement between that understanding and the outside world. That is exactly the gap
C5.1 exists to close, and the reason §1 is careful not to claim the new schema harness closes it.

Fixed, with `Gstr1SchemaValidationTests` asserting that each wrong shape fails validation, so neither
can come back quietly.

**Four further divergences are known and deliberately left alone** (open-questions.md #23): the B2C
section shape (the portal's is a flat `b2cs` array, not our nested `b2c`), the absent `b2cl` section
for inter-state B2C invoices above the invoice-wise threshold, the top-level `gtotals` block (the
portal carries `gt` / `cur_gt`), and the `uqc` hardcoded to `NOS`. These change *what* is reported
rather than how a field is spelled, so guessing at them from secondary sources would risk trading
one wrong shape for another. They need the authoritative schema and the owner's accountant.

### 5.4 GSTR-9 silently dropped invoices when the period did not start on the 1st

`BuildGstr9Async` stepped its monthly window forward with `fromDate.AddMonths(1)`. With a period
starting on the 1st — which is the only thing the screen passes, since it uses the financial-year
start — the windows tile the year correctly. With any other start they do not: a period from 15
April put the second window at 15 May – 31 May, so **an invoice dated 1–14 May belonged to no
window at all** and disappeared from the monthly rows and from the annual totals derived from them.

Confirmed by the new test before the fix: ₹1,200 of invoices in range reported as ₹500.

**Fix.** The loop now steps whole calendar months from the first of the opening month and clamps
each window to the requested period at both ends, so the windows tile `[fromDate, toDate]` exactly.

**Severity, stated honestly:** not currently reachable from the UI, because `GstReturnsViewModel`
passes the financial-year start. It was a live defect in a public service method whose output is
filed, and the kind that becomes reachable the moment a caller passes an arbitrary period — which
Phase 9's multi-user work or any reporting addition could do.

### 5.5 The pattern worth noting

§5.1, §5.2 and the three Phase 1 defects in `phase-1-evidence.md` §6 share a shape: a path that only
runs the *second* time something happens (a rate superseded, a voucher posted after the first), or
only on a *fresh* database, or only when a figure appears on **both** sides of a statement.
Fixture-based tests arrange one happy state and never visit any of them.

§5.3 and §5.4 add a second shape, and it is the more uncomfortable one: **a test written from the
same understanding as the code it tests cannot find an error in that understanding.** The export
test asserted the field names the exporter wrote, so `csft` looked correct to both. The only cures
are an external contract to check against — which is what C5.1 asks for and #23 blocks — or a test
derived from a different source than the implementation. Where neither is available, the honest
move is to say so in the evidence rather than let a green suite imply more than it proves.

---

## 6. Owner walkthrough checklist (screenshots pending)

Screenshots are `NOT CAPTURED` — they need the app running interactively. Note the walkthrough
should be done against the **current ADR-0006/0007 shell**, not the ADR-0005 one the spec
describes.

- [ ] Masters → HSN/SAC codes: add the codes the business actually sells against (nothing is seeded)
- [ ] Masters → GST rate schedules: add a rate; then supersede it with a later effective date and
      confirm the earlier row closes rather than being refused (C5.5, and the §5.1 regression)
- [ ] Post an intra-state invoice and an inter-state one; confirm CGST/SGST vs IGST and the
      round-off line (C5.2)
- [ ] GST → Returns: GSTR-1 for a period, with Table 12 B2B/B2C HSN split
- [ ] Export the GSTR-1 JSON and **open it in the GSTN offline utility** — the step that
      would close C5.1 (§1)
- [ ] GSTR-3B for the same period; check it against the GSTR-1 and the trial balance (C5.3)
- [ ] GSTR-9 for the year — now covered by tests (§4); check the twelve rows sum to the year on real data
- [ ] GST → 2B reconciliation: import a real GSTR-2B and review the three buckets (C5.4)
