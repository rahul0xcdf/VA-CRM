# Payroll: salary vouchers & statutory basics

**Phase 7b · TDS/TCS, Payroll, Banking** (`specs/phase-7-tds-payroll-banking.md` §4a D3)

## Feature

Enough payroll for compliance output, and deliberately not a payroll product (`project.md` §5).

- **Employee master** — code, name, designation, PAN, UAN, ESI number, joining and leaving dates.
  Leaving is a *date*, not a flag: a leaver's final part-month still has to compute, their PF still
  has to be remitted, and last year's pay slips still have to be reproducible. `IsOnRollOn(date)`
  answers the question every run actually asks.
- **Pay heads as configuration** — each with a type (earning / deduction / **employer
  contribution**) and a computation basis (flat amount, percent of basic, percent of gross,
  statutory), and the ledger it posts to. Employer contribution is a third type rather than folded
  into the other two because it is neither: it never reaches the employee and never reduces net
  pay, but it is a real expense and a real liability.
- **Dated salary structures.** A raise appends a line from its own date and closes the previous one,
  so September's pay slip is unchanged by an October raise.
- **PF, ESI and Professional Tax as effective-dated config**, each behind its own flag. Contribution
  rates, wage ceilings and PT slabs are **data rows** — §2 extends non-negotiable #6 to all of them
  by name. The two ceilings do opposite things and are applied in that order: an *eligibility*
  ceiling excludes the employee from the scheme outright (ESI), a *wage* ceiling merely caps the
  base so a high earner contributes on the capped wage rather than on nothing (PF). PT is charged in
  slabs rather than at a rate, because the statute is written that way; the upper bound of a slab is
  exclusive so two adjacent bands cannot both claim one wage.
- **Checkpoint C7.7 is the design, not a side effect.** With every flag off, **no statutory row is
  produced and no statutory code path runs** — the engine does not read a rate table and post a
  zero. A zero line would claim the scheme applies and happened to cost nothing, which is false. The
  challan report returns an empty list rather than three zero rows for the same reason.
- **A salary voucher posts through the journal engine**, exactly as the Phase 2 stock voucher does:
  gross to expense, each deduction to its own liability, employer contributions to both an expense
  and the matching statutory payable, and net pay to the salary payable.
- **Challan-ready output** — what to remit per statute for the period, with both shares and the
  employee count, which is the figure the owner pays from.

**Out of scope, deliberately and on the record**: Form 24Q and Form 16. Those are *returns*, they
need the TDS engine's return machinery first, and they are a phase's work of their own.

## User workflow

1. **Payroll → Payroll setup** — declare which statutes apply (all three off by default), name the
   salary payable ledger, add pay heads, then add each statute's contribution rate with its
   ceilings, or its PT slabs. Leave a slab's upper bound blank for the open top band.
2. **Payroll → Employees** — the master, then the salary structure: pick a pay head, enter an
   amount or a percentage, and the date it starts from. A statutory head takes neither figure — the
   rate and slab rows produce it.
3. **Payroll → Payroll run** — pick the month end and press Compute. The pay slips appear with
   totals, and any warning ("PF is on but no rate is in force this month") is shown rather than
   swallowed. `Ctrl+Enter` opens the challan roll-up. Press Post when the figures are right.

## Developer notes

- Domain: `Employee`, `PayHead`, `SalaryStructureLine`, `StatutoryRateSchedule`,
  `ProfessionalTaxSlab`, `PayrollSettings`, `SalaryVoucher` + `SalaryVoucherLine`, and the pure
  arithmetic in `PayrollMath` (`Domain/Payroll/`).
- Application: `IPayrollMasterService` (masters and statutory config), `IPayrollRunService`
  (preview, post, pay slips, challan) — `Application/Payroll/`.
- Order of computation is load-bearing: basic settles first (percent-of-basic depends on it), gross
  is totalled, then deductions and statutory contributions. That is also the order a slip reads in.
- `SalaryVoucherLine` is a snapshot, like a posted tax deduction: the head's name, type and statute
  are frozen at posting time so a pay slip issued in May says in December what it said in May.
- Voucher totals are computed projections, never stored columns — non-negotiable #4 applies to a
  payroll total exactly as it does to a ledger balance.
- One run per month, enforced by a unique index on `(CompanyId, PeriodEnd)`: a second run for a
  month already paid would be a duplicate set of challans nobody reconciles.
- `PayrollRunService.BasicPayHeadName` is the one name the engine knows, and it knows it because
  `PercentOfBasic` is meaningless without an agreed answer to "which head is basic".
- Migration `AddPayroll`. `EnsurePayrollSeedAsync` provisions the salary and statutory ledgers and
  the settings row, and seeds **no rate, ceiling, slab or pay head**.

## Verification

- `PayrollMathTests` — hand-computed fixtures for both ceilings and the order they apply in, the
  slab lookup including boundary and below-the-lowest-band cases, the three non-statutory bases,
  whole-rupee rounding, and the statutory basis being refused rather than silently returning zero.
- `PayrollWorkflowTests` — the slice against a real SQLite file: **C7.7** (rates and slabs present
  and in force, flags off, and nothing statutory is produced anywhere including the challan report),
  one flag on producing that statute and no other, a balanced journal voucher with the pay slip
  frozen, a re-run refused, the challan split across both shares, a dated rate change, a raise not
  disturbing an earlier month, a leaver dropping out after their last day, and the
  flag-on-no-rate warning.
