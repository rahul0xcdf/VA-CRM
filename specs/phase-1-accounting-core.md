# Phase 1 — Accounting Core

```yaml
Phase: 1
Title: Accounting Core
Status: Approved
Approved by: Project owner, 14 Aug 2026
Source: project.md §4.1, §4.6, §10 (Phase 1)
Depends on: Phase 0
Blocks: Phases 2–10
Decisions recorded: ADR-0001 (layering), ADR-0002 (append-only journal), ADR-0003 (money storage)
```

> **Approved 14 Aug 2026 by the project owner**, who instructed that implementation proceed on the
> defaults this spec proposes, to be corrected after they have used the running application. The
> `Status` change is recorded here as the owner's decision, not the architect's.
>
> **What was decided, and what was deferred to first use.** Two blocking items were answered
> outright: money storage (**ADR-0003 accepted** — scaled 64-bit integer minor units, see §6) and
> the D1–D4 decisions in §3. The remaining §16 items proceed on the defaults proposed in this
> file — the seed ledger list in §4.5, the numbering prefixes in §7.2, and the audit-log hash chain
> in §11 — on the explicit understanding that the owner will review them against the running
> product and correct them.
>
> **Two of those defaults are cheap to change now and expensive later**, and are flagged for
> attention at first review rather than left silent:
> - **Numbering prefixes** (`PMT/0001`, `RCT/0001`, `SAL/0001`, …) end up printed on documents given
>   to customers and filed with returns. Changing them after real vouchers exist means either a gap
>   in the series or a renumbering, and GST invoice numbering is not freely re-writable.
> - **Seed ledgers** are simple to add later, but any that are wrong and have already been *posted
>   to* cannot simply be deleted — the append-only journal (ADR-0002) has no delete path.
>
> Implementation order follows the owner's instruction to build a usable vertical slice first:
> chart of accounts → ledger masters → Payment/Receipt/Journal entry → day book and trial balance,
> so the journal engine and audit trail can be judged in use before the remaining voucher types and
> statements are built out.

## 1. Goal

Build the ledger. Everything the rest of the product does — stock valuation (Phase 2), invoicing
(Phase 3), GST returns (Phase 5), Tally export (Phase 8) — eventually lands as a balanced set of
journal lines, so this phase decides how a rupee is recorded, how it is proved, and how a change to
it is evidenced. The two properties that must hold forever after this phase are:

1. **Every reported number is derived from journal lines.** There is no column anywhere that holds a
   balance and can be written independently.
2. **Nothing that has been posted is ever silently changed.** An alteration writes a new revision and
   an edit-log row; a posted journal line is never updated in place and never deleted.

Retrofitting either property later would mean rewriting every report and every posting path, which
is why they are settled here and not in Phase 5 when GST makes them urgent.

## 2. Deliverables (project.md §10, Phase 1)

Masters (groups / ledgers), all core voucher types, the journal engine, day book / trial balance /
balance sheet / P&L, and the edit-log / audit trail.

The screens `ui-agent` builds in this phase, and no others:

| Screen | Notes |
|---|---|
| Account group master (list + edit) | Tree view; system groups shown but locked (§4.6) |
| Ledger master (list + edit) | Grouped by account group; search by name and alias |
| Voucher entry | One screen parameterised by voucher type, not eight screens; Dr/Cr grid with a running difference that must read zero before Save is enabled |
| Voucher alteration / cancellation | Same screen; writes a new revision (§8.2) and surfaces the history behind a "View history" action |
| Day book | Date range; cancelled vouchers shown and flagged |
| Trial balance | With drill-down to `Accounting.LedgerVouchers.ForPeriod` |
| Profit & Loss, Balance Sheet | As of / for a period, with the same drill-down |
| Bank reconciliation | Manual marking only (§12) |
| Edit log viewer | **View-only.** No delete, no purge, no filter that can hide rows permanently |
| Voucher-number gap report | §8.7 |

No cost-centre screen (D3), no report export, no print. Reports are on-screen grids; export is
Phase 8 (ClosedXML) and printing is Phase 3 (FastReport).

## 3. Decisions taken by the project owner

These close items that were listed as open in the Phase 1 stub and in `open-questions.md`.

| # | Decision | Recorded in |
|---|---|---|
| D1 | **Financial year starts 1 April** (Indian standard). `Company.FinancialYearStartMonth` stays `4`; the column remains because the schema is multi-company-ready and the value is data, not a constant. | `open-questions.md` #9 — Confirmed |
| D2 | **Voucher numbering:** per-voucher-type series with prefix + width; the counter **resets each financial year**; **gaps are blocked** — deleting or cancelling a voucher does not free its number; any gap that does arise is surfaced in an audit report rather than silently reused; a **manual number override is permitted and is written to the edit log**. Closest to TallyPrime's default behaviour. | §7, `open-questions.md` #11 — Confirmed |
| D3 | **Cost centres: schema now, UI hidden.** `CostCategory`, `CostCentre` and the **nullable** `JournalLine.CostCentreId` ship in the Phase 1 migration; **no UI is built in Phase 1**. | §9, `open-questions.md` #12 — Confirmed |
| D4 | **Product display branding is "Vinayaka Agencies."** ~~The solution and root namespace remain `MotorPumpSuite`; a namespace rename is still an open owner decision and is cheapest before Phase 1 implementation starts.~~ **Superseded 15 Aug 2026:** the owner elected to rename. The solution, root namespace and all seven projects are now `VinayakaAgencies`; the display name is unchanged at "Vinayaka Agencies". Done immediately before the Phase 1 migration was generated, which is the point §16 identified as cheapest. | `open-questions.md` #7 — **Resolved, renamed** |

## 4. Chart of accounts

### 4.1 Model

Two entities, mirroring Tally's own separation: **`AccountGroup`** (a classification node) and
**`Ledger`** (a postable account). Journal lines post to a `Ledger`, never to an `AccountGroup`.
Groups nest to arbitrary depth; ledgers hang off exactly one group and are leaves.

### 4.2 Nesting: parent id + materialised path

**Decision: adjacency (`ParentGroupId`) as the authoritative structure, plus a materialised path
column (`MaterialisedPath`) as a derived, rebuildable index of that structure.**

`MaterialisedPath` is the `/`-separated chain of ancestor GUIDs in `N` format, root first, with a
leading and trailing `/` — e.g. `/a1b2…/c3d4…/`. GUIDs rather than names, because a group rename
must not touch the path of thousands of descendants. Depth is capped at **10**, which makes the
column `varchar(400)` — comfortably inside SQL Server's 900-byte index key limit at Phase 9, and
inside SQLite's practical limits today.

**Why this and not the alternatives:**

| Option | Read cost (roll-up) | Write cost (add/move a group) | Verdict |
|---|---|---|---|
| Adjacency only (`ParentGroupId`) | Recursive CTE per report | O(1) | Rejected — the recursive CTE is a second place where SQLite and T-SQL diverge, and ADR-0001 exists to minimise exactly that. It is also the slowest option for a wide roll-up. |
| Nested set (left/right) | O(log n) range scan | O(n) — every insert renumbers a large slice of the table | Rejected — group masters are edited rarely, but a whole-table renumber under Phase 9's concurrent access is a corruption hazard for no read-side gain over a path. |
| **Adjacency + materialised path** | O(log n + k) range scan | O(subtree) on re-parent only | **Chosen** |

**Roll-up query cost.** A group's balance is every ledger whose group's path starts with that group's
path. The dialect SQL uses a half-open **range predicate**, not `LIKE`:

```
MaterialisedPath >= @prefix AND MaterialisedPath < @prefixUpperBound
```

where `@prefixUpperBound` is `@prefix` with its last character incremented. This is guaranteed
index-sargable on both SQLite and SQL Server regardless of collation, whereas `LIKE 'x%'` is only
sargable under some collations. Cost is one index seek plus the matched rows.

**Consistency.** `MaterialisedPath` is a cache of the parent chain, recomputed for the whole affected
subtree inside the *same* `IUnitOfWork` transaction as any `ParentGroupId` change. A maintenance
routine `RebuildAccountGroupPathsAsync` can rebuild every path from `ParentGroupId` alone, and a test
(C1.14) asserts the two agree for a randomised tree. This is not a stored balance — the
"balances are derived" rule (§8) is untouched.

### 4.3 Predefined primary groups (seeded, system-locked)

Fifteen primary groups, matching TallyPrime so that Phase 8 export maps one-to-one. `Nature` drives
which statement a balance lands on; `AffectsGrossProfit` drives the gross-profit line of the P&L.

| Primary group | Nature | Affects gross profit |
|---|---|---|
| Branch / Divisions | Liability | No |
| Capital Account | Liability | No |
| Current Assets | Asset | No |
| Current Liabilities | Liability | No |
| Direct Expenses | Expense | Yes |
| Direct Incomes | Income | Yes |
| Fixed Assets | Asset | No |
| Indirect Expenses | Expense | No |
| Indirect Incomes | Income | No |
| Investments | Asset | No |
| Loans (Liability) | Liability | No |
| Misc. Expenses (ASSET) | Asset | No |
| Purchase Accounts | Expense | Yes |
| Sales Accounts | Income | Yes |
| Suspense A/c | Liability | No |

Suspense is deliberately shown on the balance sheet rather than hidden: a non-zero suspense balance
is a defect the owner should be able to see.

### 4.4 Predefined sub-groups (seeded, system-locked)

| Sub-group | Parent |
|---|---|
| Bank Accounts | Current Assets |
| Bank OD A/c | Loans (Liability) |
| Cash-in-Hand | Current Assets |
| Deposits (Asset) | Current Assets |
| Duties & Taxes | Current Liabilities |
| Loans & Advances (Asset) | Current Assets |
| Provisions | Current Liabilities |
| Reserves & Surplus | Capital Account |
| Secured Loans | Loans (Liability) |
| Stock-in-Hand | Current Assets |
| Sundry Creditors | Current Liabilities |
| Sundry Debtors | Current Assets |
| Unsecured Loans | Loans (Liability) |

### 4.5 Seed ledgers — **for owner approval, line by line**

This is the open item the stub flagged. Proposal below; the owner may strike or add any row. Keep it
minimal: a ledger that exists but is never used is clutter in every dropdown for the life of the
product, and every one of these is trivial for the owner to create later.

`Locked` means `IsSystem = true`: the row cannot be deleted or re-parented. Renaming is allowed
except where stated. Even system rows' changes are edit-logged (§11) — locked does not mean invisible.

| # | Ledger | Parent group | Locked | Why | Recommended |
|---|---|---|---|---|---|
| L1 | **Cash** | Cash-in-Hand | No delete; rename allowed | Every business posts cash on day one; Tally predefines it, so Phase 8 export maps cleanly | **Yes** |
| L2 | **Profit & Loss A/c** | *(top level, no parent)* | Fully locked — no rename, no delete, no manual posting | The balance-sheet carrier for net profit. Manual posting is blocked because its balance must always be derived (§8.4) | **Yes** |
| L3 | **Opening Balance Difference** | Suspense A/c | No delete, no rename | Balancing counterpart of the opening-balance voucher (§8.5) when migrated opening figures do not net to zero. A non-zero balance here is a visible, intentional red flag rather than a silent plug | **Yes** |
| L4 | **Stock-in-Hand** | Stock-in-Hand | No delete | Holds the opening stock **value** in Phase 1; becomes the inventory control account that Phase 2's checkpoint C2.1 reconciles against. Seeding it now avoids a Phase 2 data fix-up | **Yes** |
| L5 | **Sales** | Sales Accounts | User-editable | Default revenue ledger for the Sales voucher type | **Yes** |
| L6 | **Purchases** | Purchase Accounts | User-editable | Default ledger for the Purchase voucher type | **Yes** |
| L7 | **Round Off** | Indirect Expenses | User-editable | Needed from Phase 3 invoicing. Harmless as a data row now; creating it later is equally cheap | Owner decides |
| L8 | **Discount Allowed** | Indirect Expenses | User-editable | Common in this trade | Owner decides |
| L9 | **Discount Received** | Indirect Incomes | User-editable | Common in this trade | Owner decides |
| L10 | *Bank account(s)* | Bank Accounts | — | **Not seeded.** A bank ledger needs the real account name, number and IFSC; a placeholder called "Bank" is worse than none | **No** |
| L11 | *Party ledgers* | Sundry Debtors / Creditors | — | **Not seeded.** Customer and supplier ledgers arrive with the customer master in Phase 4 | **No** |
| L12 | *CGST / SGST / IGST, input and output* | Duties & Taxes | — | **Not seeded.** GST is Phase 5, and rates/codes must be data-driven config rows with effective dates (`project.md` §4.5). Seeding tax ledgers here would invite someone to hardcode a rate against them | **No** |

### 4.6 Master lifecycle: deactivate vs delete

- **Deactivate** (`IsActive = false`) is always available and is the normal path. An inactive master
  is hidden from pickers but keeps every historical posting intact.
- **Hard delete** is permitted only when the row has no journal line, no child group/ledger, and no
  other reference. It writes an `Action = Delete` edit-log row carrying the **full before-image**, so
  the deleted row is reconstructible from the log alone.
- **System-locked rows** (`IsSystem = true`) reject both delete and re-parent with a domain exception.

## 5. Entities and schema

All tables carry `CompanyId` (§10). All `Id` values are client-generated GUIDs on `Entity`
(ADR-0001 §5), with one deliberate exception: `AuditLogEntry` (§11.4). Money and quantity are
`decimal` in the domain — see §6 for how they are persisted.

| Entity | Key columns | Notes |
|---|---|---|
| `AccountGroup` | `CompanyId`, `Name`, `ParentGroupId?`, `MaterialisedPath`, `Nature`, `AffectsGrossProfit`, `IsPrimary`, `IsSystem`, `SortOrder`, `IsActive` | Unique `(CompanyId, ParentGroupId, NameNormalised)`. Index on `(CompanyId, MaterialisedPath)`. `MaterialisedPath` is capped at 400 chars — see §4.2 and the note below this table |
| `Ledger` | `CompanyId`, `Name`, `Alias?`, `AccountGroupId`, `IsSystem`, `AllowsManualPosting`, `Description?`, `IsActive` | Unique `(CompanyId, NameNormalised)`. **No opening-balance column** — see §8.5. Whether a ledger is a bank ledger is *derived* from its group being at or under `Bank Accounts` / `Bank OD A/c`, not stored |
| `FinancialYear` | `CompanyId`, `StartDate` (`DateOnly`), `EndDate` (`DateOnly`), `Label`, `IsClosed` | Unique `(CompanyId, StartDate)`. Created lazily on first post into a year, derived from `Company.FinancialYearStartMonth` |
| `PeriodLock` | `CompanyId`, `LockedThroughDate` (`DateOnly`), `Reason`, `SetBy`, `SetAtUtc` | One row per company; changing it is a master change and is edit-logged |
| `VoucherType` | `CompanyId`, `Name`, `Kind`, `IsSystem`, `DefaultSeriesId?`, `IsActive` | Unique `(CompanyId, NameNormalised)`. See §7.1 for why `Kind` is an enum while the type itself is a data row |
| `VoucherNumberSeries` | `CompanyId`, `VoucherTypeId`, `Name`, `Prefix`, `Suffix`, `Width`, `StartNumber`, `ResetPolicy`, `AllowManualOverride`, `IsActive` | §7.2 |
| `VoucherNumberSequence` | `SeriesId`, `FinancialYearId`, `LastAllocatedNumber` | Unique `(SeriesId, FinancialYearId)`. High-water mark; never decreases |
| `Voucher` | `CompanyId`, `VoucherTypeId`, `SeriesId`, `FinancialYearId` | Immutable identity only. This GUID is the stable id Phase 8 exports to Tally |
| `VoucherRevision` | `VoucherId`, `RevisionNumber`, `Status`, `VoucherDate` (`DateOnly`), `Number`, `NumberNormalised`, `IsManualNumber`, `Narration?`, `ReferenceNumber?`, `ReferenceDate?`, `ChangeReason?`, `SupersededByRevisionId?`, `CreatedAtUtc`, `CreatedBy`, **`CompanyId`, `VoucherTypeId`, `SeriesId`, `FinancialYearId`** | Unique `(VoucherId, RevisionNumber)`. Filtered unique index on `(CompanyId, VoucherTypeId, SeriesId, FinancialYearId, NumberNormalised) WHERE SupersededByRevisionId IS NULL`. See ADR-0002. **The last three key columns are denormalised copies of the parent `Voucher`'s** — amended 15 Aug 2026 after `architect-agent` review found the index unbuildable otherwise, since neither provider can put a unique index across a join. They are set in the constructor from the parent, are `private set`, and are never mutated; all three are immutable on `Voucher`, so the copies cannot drift by construction. `FinancialYearId` is mandatory in the key, not optional: D2 resets the counter each financial year, so `PMT/0001` legitimately recurs and a company-wide unique index would reject the second year's first payment |
| `JournalLine` | `CompanyId`, `VoucherRevisionId`, `LineNumber`, `LedgerId`, `AmountMinor`, `CostCentreId?`, `Narration?` | Insert-only. Signed amount: positive = debit, negative = credit (§8.1). Index `(CompanyId, LedgerId)` and `(VoucherRevisionId, LineNumber)` |
| `CostCategory` | `CompanyId`, `Name`, `IsActive` | D3 — schema only, no UI |
| `CostCentre` | `CompanyId`, `CostCategoryId?`, `Name`, `ParentCostCentreId?`, `MaterialisedPath`, `IsActive` | D3 — schema only, no UI |
| `BankReconciliationMark` | `CompanyId`, `JournalLineId`, `BankDate` (`DateOnly`), `ClearedAtUtc`, `ClearedBy` | §12. Unique on `JournalLineId` |
| `AuditLogEntry` | `Sequence`, `CompanyId?`, `OccurredAtUtc`, `UserName`, `MachineName`, `SaveBatchId`, `Action`, `EntityType`, `EntityId`, `AggregateType?`, `AggregateId?`, `ChangedProperties`, `BeforeJson?`, `AfterJson?`, `PreviousHash`, `RowHash` | §11. Insert-only at every level from the domain type up to a database trigger |

**Column lengths — added 15 Aug 2026 after `architect-agent` review.** `AppDbContext.ConfigureConventions`
defaults every non-key string to 512 characters so nothing maps to `nvarchar(max)` by accident at
Phase 9. SQLite ignores the cap, so a wrong value is invisible through Phase 8 and only bites on the
provider switch. Three cases must therefore be set explicitly rather than left on the default:

| Property | Length | Why |
|---|---|---|
| `AuditLogEntry.BeforeJson`, `.AfterJson`, `.ChangedProperties` | unbounded | §11.1 stores **full row images**. At `nvarchar(512)` they would truncate at Phase 9 — silent audit-log data loss on a table whose triggers block repair. |
| `AccountGroup.MaterialisedPath`, `CostCentre.MaterialisedPath` | 400 | §4.2 sized this for SQL Server's 900-byte index key limit. At the 512 default the `(CompanyId, MaterialisedPath)` index is 1040 bytes and **SQL Server refuses to create it**. |
| `AuditLogEntry.PreviousHash`, `.RowHash` | 64, fixed | SHA-256 hex. |

`EntityId` 64; `EntityType` / `AggregateType` 256; `UserName` / `MachineName` / `CreatedBy` /
`ModifiedBy` 256, matching the existing `CompanyConfiguration`. `Narration` is widened to 1000 —
512 is tight for an operator used to Tally, and the column costs nothing on SQLite.

**Delete behaviour.** Every relationship in this phase is configured `DeleteBehavior.Restrict`,
explicitly and without exception. EF Core defaults required relationships to `Cascade`, which would
put a cascade-delete path through `JournalLine` — a delete path for posted lines, which ADR-0002
forbids outright. `Restrict` on `Ledger → JournalLine` also gives §4.6's "hard delete only when the
row has no journal line" a database-level backstop instead of a validation someone must remember.

**Phase 1 ships two migrations, not one.** The architect's review called for a single migration
covering all fourteen tables. In implementation the audit-log table, its indexes and its two
`RAISE(ABORT)` triggers landed first, as `AddAuditLogTable`, because the audit capture in
`SaveChangesAsync` cannot be tested at all until the table it writes to exists — and §11's defences
are the last thing that should ship untested. The accounting tables follow in a second migration.
Nothing in the review's reasoning is violated: its hard rule was that no column on `JournalLine` or
`VoucherRevision` may be deferred (§9, §13), and neither is. §15's evidence requirement is read as
covering the DDL of **both** Phase 1 migrations. Isolating the edit log's DDL arguably helps the
reviewer who has to satisfy themselves that the triggers exist and that no balance column does.

**Seed data does not use EF's `HasData`.** The §4.3 primary groups, §4.4 sub-groups, §4.5 ledgers and
§7.1 voucher types with their series are written at runtime by a company-provisioning service going
through `IUnitOfWork`, not baked into the migration. Three reasons, any one sufficient: `HasData`
writes rows through the migration and therefore **bypasses `SaveChangesAsync` and the audit trail**,
which §11.1 requires for every insert of every mapped entity — the creation of a system-locked row is
exactly what an auditor asks about; it bypasses `IAuditStamped`, so `CreatedBy` would be a literal
string; and every one of these rows is `CompanyId`-scoped while companies are created at runtime, so
per-company seed data is not expressible as static migration data at all. Provisioning a company
therefore produces roughly fifty audit rows, which is the correct outcome rather than a cost.

**Enum columns are stored as strings** (`HasConversion<string>()`), for the reason §11.3 already
gives about the audit log: a later enum reordering must not silently change the meaning of existing
rows. This applies to `AccountNature`, `VoucherKind`, `VoucherStatus`, `NumberResetPolicy` and
`AuditAction`, and applies harder because Phase 5 and Phase 8 reporting SQL filters on them.

**`CompanyId` is a real foreign key with no navigation property** — `HasOne<Company>().WithMany()`.
Referential integrity for §10's cross-company rule belongs in the database, not only in the §8.1
post-time validation. Omitting the navigation keeps §11.3's "navigation properties are never
serialised" trivially true. Two exceptions: `AuditLogEntry.CompanyId` gets **no** FK, because the log
must outlive any company row; and `VoucherType.DefaultSeriesId` gets no FK, because
`VoucherType → VoucherNumberSeries → VoucherType` is a cycle that would force EF to split the insert
into an insert-with-null plus a later `UPDATE`, generating a spurious audit row on every company
provisioning.

## 6. Money: exactness under SQLite — **needs ADR-0003 before implementation**

`CLAUDE.md` says money and quantity are `decimal(18,4)` and never `double`/`float`. Phase 1 is where
that convention meets a hard fact about the Phase 1–8 database:

**SQLite has no decimal type.** EF Core maps `decimal` to a `TEXT` column. Dapper reporting queries
must aggregate in SQL — `SUM(Amount)` over a `TEXT` column makes SQLite apply numeric affinity and
sum in **REAL, i.e. IEEE-754 double**. Every trial balance, every P&L, every balance sheet in
Phases 1–8 would therefore be computed in floating point, which is exactly what the convention
forbids and exactly the class of defect that makes a trial balance fail to total zero by ₹0.0001.

**Architect's recommendation:** money and quantity stay `decimal` in the domain and in every DTO, and
are **persisted as a scaled 64-bit integer in minor units of 1/10,000** (`AmountMinor`) via an EF Core
value converter under SQLite. `long.MaxValue / 10^4` is about 9.2 × 10^14 rupees, which is not a
practical ceiling. Aggregation in SQL is then exact integer arithmetic, the balance check
`Σ AmountMinor == 0` is exact with no epsilon, and the SQLite/SQL Server difference is confined to
the dialect layer where ADR-0001 already puts provider differences. Reporting row DTOs carry the
`long` minor units and are converted once, in one mapping helper, to `decimal`.

Trade-off accepted: report DTOs are less readable, and anyone reading the SQLite file with a generic
tool sees integers, not rupees. That is a smaller price than an inexact ledger.

**This reinterprets a convention stated in `CLAUDE.md`, so it requires its own ADR (ADR-0003) written
and accepted before Phase 1 implementation begins.** It is in the sign-off checklist (§15) and in
`open-questions.md` #13. Do not implement money storage until that ADR exists.

## 7. Voucher types and numbering

### 7.1 Voucher types

Eight system `VoucherType` rows are seeded per company, one per kind:

| Kind | Seeded type | Typical posting shape |
|---|---|---|
| `Payment` | Payment | Credit a cash/bank ledger, debit one or more others |
| `Receipt` | Receipt | Debit a cash/bank ledger, credit one or more others |
| `Contra` | Contra | Cash ↔ bank, or bank ↔ bank, only |
| `Journal` | Journal | Any ledgers; no cash/bank restriction |
| `Sales` | Sales | Debit party/cash, credit sales ledger |
| `Purchase` | Purchase | Credit party/cash, debit purchase ledger |
| `CreditNote` | Credit Note | Reverse of Sales |
| `DebitNote` | Debit Note | Reverse of Purchase |

`VoucherType` is a **data row**; `Kind` is a C# enum. The distinction matters and is deliberate: the
*kind* determines code behaviour (which validation rules apply, which statement a voucher register
belongs to), so it must be a closed set the compiler knows about. The *type* is user-extensible —
the owner can add "Cash Sales" and "Credit Sales" as two types under `Kind = Sales`, each with its
own numbering series, exactly as Tally allows. This is not a violation of the "nothing hardcoded"
rule, which is about tax data — HSN/SAC codes, GST rates, TDS/TCS sections, rates and thresholds —
none of which appear in Phase 1.

Kind-specific validation in Phase 1 is limited to Contra (both sides must be cash or bank ledgers).
Restricting Payment/Receipt to a cash/bank ledger on one side is Tally behaviour but is easy to get
wrong for legitimate entries, so it is a **warning, not a rejection**, in Phase 1.

### 7.2 Numbering (decision D2)

- A **`VoucherNumberSeries`** belongs to one `VoucherType` and defines `Prefix`, optional `Suffix`,
  zero-padded `Width`, `StartNumber`, and `ResetPolicy` (`FinancialYear` by default per D2, `Never`
  available). Example: prefix `PMT/`, width 4, FY 2026-27 → `PMT/0001`.
- A **`VoucherNumberSequence`** row holds `LastAllocatedNumber` for one `(Series, FinancialYear)`
  pair. Reset-on-FY is achieved by the sequence being keyed on the financial year — there is no
  scheduled job and no "reset" button that could be pressed at the wrong moment.
- **Allocation happens at post time**, inside the same `IUnitOfWork` transaction that writes the
  voucher and its lines. A voucher that fails validation never consumes a number, because validation
  runs before allocation.
- **Gaps are blocked.** Cancelling a voucher (§8.6) keeps its number: the cancelled revision is still
  the current revision, still holds the number, and still occupies the filtered unique index.
  Vouchers are never deleted, so a number is never released. A gap can therefore only arise from a
  manual override that skips ahead, or from a future data import — and both are surfaced by the
  `Accounting.VoucherNumberGaps` report (§8.7) rather than silently backfilled.
- **Manual override is permitted** when the series has `AllowManualOverride = true` (default). The
  supplied number must not collide with an existing current-revision number in the same series and
  financial year — the filtered unique index enforces this even if a validation is bypassed. If the
  override is numerically above the high-water mark, `LastAllocatedNumber` jumps to it so that
  subsequent automatic numbers continue after it and never collide. The override is recorded by
  `IsManualNumber = true` on the revision *and* by the edit-log row for the revision, whose
  after-image contains both the number and the flag. Renumbering an already-posted voucher is
  possible and is done the same way every other alteration is done — a new revision (ADR-0002) — so
  the old number and the new one are both permanently visible in the log.
- **Concurrency is deferred to Phase 9.** Phase 1–8 is single-user (`open-questions.md` #4), so
  read-modify-write of `LastAllocatedNumber` inside the posting transaction is sufficient. Phase 9
  must revisit this: under SQL Server with concurrent clients, allocation needs either an
  `UPDATE … OUTPUT` on the sequence row or a row-level lock, and the filtered unique index becomes
  the last line of defence rather than a formality. This is flagged in §13.

## 8. Journal engine and derived balances

### 8.1 Posting

A voucher posts a set of `JournalLine` rows against a `VoucherRevision`. Each line carries a
**signed amount**: positive is a debit, negative is a credit. One signed column rather than separate
`Debit`/`Credit` columns, because it makes the fundamental invariant a single exact comparison and
makes it structurally impossible to write a row with both a debit and a credit set.

**Balance rule.** Posting is rejected unless `Σ AmountMinor == 0` across the revision's lines —
exactly zero in integer minor units at scale 4 (§6). No tolerance, no epsilon. Additional posting
validations, all of which reject:

- at least two lines, and at least one debit and one credit;
- no line with a zero amount;
- every `LedgerId` belongs to the same `CompanyId` as the voucher (no cross-company posting);
- every target ledger is active and has `AllowsManualPosting = true`;
- `VoucherDate` falls inside an open `FinancialYear` for the company and after `PeriodLock.LockedThroughDate`;
- Contra: every ledger is a cash or bank ledger.

### 8.2 Append-only alteration — see ADR-0002

`Voucher` is a thin, permanently immutable identity. All content lives on `VoucherRevision`, and
`JournalLine` rows belong to a revision. Altering a voucher writes **a new revision with a new set of
lines** and sets `SupersededByRevisionId` on the previous revision — the one and only column in the
whole design that transitions, once, from `NULL` to a value, and never changes again. No posted
`JournalLine` is ever updated or deleted.

The **current** state of a voucher is the revision where `SupersededByRevisionId IS NULL`. Every
accounting query filters on that predicate; the reason it is a predicate rather than a boolean flag
is that it simultaneously records *what* superseded a revision, which the edit-log viewer needs.
A partial (SQLite) / filtered (SQL Server) index supports it on both providers.

### 8.3 The derived-balance invariant

**No table in this schema has a balance column, and none ever will.** Not on `Ledger`, not on
`AccountGroup`, not on `Company`. Ledger balances, group balances, the trial balance, the P&L and
the balance sheet are all `SUM(AmountMinor)` over `JournalLine`, joined to the current, non-superseded
`VoucherRevision`, filtered by company and business date. A reviewer can check this claim in one
step: grep the migration for a column named `*Balance*` or `*Total*` on a master table. There is none.

Carry-forward across financial years is likewise derived: the opening balance of a ledger on a date
is the sum of all its lines strictly before that date. There is no year-end "closing" posting that
freezes a number.

**Performance note and the one permitted future exception.** At this business's volume this is a
non-issue. If a future phase's profiling shows the full-history sum hurting, the permitted fix is a
**period-summary table that is a rebuildable cache** — derivable from journal lines alone, rebuilt by
a maintenance command, never the authoritative source, and never writable by business code. Anything
else is a rewrite of this invariant and needs its own ADR.

### 8.4 Statements

All reporting reads go through `ISqlQueryExecutor` with a named `SqlQueryKey` resolved per provider
by `ISqlDialect` (ADR-0001 §3). **No SQL string appears in Application or App.** The keys Phase 1
adds, declared in `VinayakaAgencies.Application/Reporting/AccountingQueryKeys.cs`:

| `SqlQueryKey` | Purpose | Parameters |
|---|---|---|
| `Accounting.LedgerBalances.AsOf` | Closing balance per ledger up to a date | `CompanyId`, `AsOfDate` |
| `Accounting.LedgerBalances.ForPeriod` | Debit and credit movement between two dates | `CompanyId`, `FromDate`, `ToDate` |
| `Accounting.TrialBalance` | Opening, Dr, Cr, closing per ledger with its group path | `CompanyId`, `FromDate`, `ToDate` |
| `Accounting.GroupBalances.AsOf` | Roll-up per group via the path range predicate (§4.2) | `CompanyId`, `AsOfDate` |
| `Accounting.ProfitAndLoss.ForPeriod` | Income and expense sections, gross and net profit | `CompanyId`, `FromDate`, `ToDate` |
| `Accounting.BalanceSheet.AsOf` | Asset and liability sections | `CompanyId`, `AsOfDate`, `FinancialYearStartDate` |
| `Accounting.DayBook.ForPeriod` | All vouchers in a date range, cancelled ones included and marked | `CompanyId`, `FromDate`, `ToDate` |
| `Accounting.LedgerVouchers.ForPeriod` | Ledger statement / drill-down from any report | `CompanyId`, `LedgerId`, `FromDate`, `ToDate` |
| `Accounting.VoucherRegister.ByType` | Voucher register for one type | `CompanyId`, `VoucherTypeId`, `FromDate`, `ToDate` |
| `Accounting.VoucherNumberGaps` | Missing numbers in a series for a financial year | `CompanyId`, `SeriesId`, `FinancialYearId` |
| `Accounting.EditLog.ForPeriod` | Edit-log viewer, optionally filtered to one entity | `CompanyId`, `FromDate`, `ToDate`, `EntityType?`, `EntityId?` |
| `Accounting.EditLog.ChainVerification` | Hash-chain integrity check (§11.5) | `FromSequence` |
| `Accounting.BankReconciliation.Statement` | Bank ledger lines with their cleared marks | `CompanyId`, `LedgerId`, `FromDate`, `ToDate` |

How each statement is computed:

- **Trial balance** — group by `LedgerId`; opening = Σ before `FromDate`; Dr = Σ of positive amounts
  in the period; Cr = Σ of negative amounts; closing = opening + Dr + Cr. Totals to exactly zero
  (C1.5).
- **P&L** — ledgers whose group `Nature` is `Income` or `Expense`, split by `AffectsGrossProfit` into
  the gross-profit block and the net-profit block, over the period.
- **Balance sheet** — ledgers whose group `Nature` is `Asset` or `Liability`, as of a date, plus the
  net profit for the year to date carried into the **Profit & Loss A/c** ledger's presented balance.
  P&L A/c has no posted lines of its own (it is `AllowsManualPosting = false`); its balance-sheet
  figure is computed, which is the whole reason it is locked against manual posting.
- **Day book** — current revisions in the date range, ordered by date then number, with cancelled
  vouchers listed and flagged (matching Tally, which shows cancelled vouchers rather than hiding
  them; hiding them would make the numbering gap-blocking rule look like a bug).

### 8.5 Opening balances

There is **no `OpeningBalance` column on `Ledger`.** Opening balances are posted as an ordinary
balanced journal voucher, dated the first day of the financial year, using a system
`Journal` voucher type with a dedicated series. Any residual difference goes to the
**Opening Balance Difference** ledger (L3).

Why: an opening-balance column would be an independently-writable balance column in everything but
name, which §8.3 forbids. Posting them as a voucher also means the Phase 8 Tally import gets them for
free, and any later correction to an opening figure is a normal alteration with a normal edit-log
entry — which is precisely the sort of change an auditor asks about.

### 8.6 Cancellation, not deletion

A voucher is never deleted. "Delete" in the UI creates a new revision with `Status = Cancelled` and
**zero journal lines**. Balances then exclude it automatically — no report needs a special case — the
number stays occupied, and the previous revision (with its lines) remains permanently readable in the
edit log.

### 8.7 Numbering-gap report

`Accounting.VoucherNumberGaps` enumerates the numeric part of every current-revision number in a
series and financial year and reports the missing values with the surrounding vouchers. This is the
audit-report half of decision D2: gaps are not reused, but neither are they hidden.

## 9. Cost centres (decision D3)

`CostCategory`, `CostCentre` and the **nullable** `JournalLine.CostCentreId` foreign key ship in the
Phase 1 migration. **No UI is built in Phase 1**, no cost-centre allocation logic runs, and no report
groups by cost centre.

Rationale, recorded here because the cost of the alternative is easy to underestimate: adding a
dimension column to `JournalLine` after it holds production rows means an `ALTER TABLE` on the largest
table in the database, backfill decisions for existing rows, an index rebuild, and — under SQLite,
which rewrites the table for many schema changes — a full-table copy of the transaction history with
the audit log watching. Shipping an unused nullable column now costs one column of NULLs.

The absence of UI is itself a checkpoint criterion (C1.13), so "unused" stays true and does not
quietly become a half-built feature.

## 10. Multi-company

Every master and every voucher carries `CompanyId`, referencing the `Company` entity built in Phase 0.
Consequences that must hold from the first migration:

- Every unique index is company-scoped — `(CompanyId, Name)`, never `(Name)`.
- Every `SqlQueryKey` in §8.4 takes `@CompanyId` as its first parameter. A reporting query without a
  company filter is a defect.
- A voucher's lines must all reference ledgers of the same company; validated at post time (§8.1).
- The Application layer resolves the active company through an `ICompanyContext`. Phase 1 ships one
  company and no company-switching UI, but the plumbing exists so that adding the second company is
  data, not a migration.

## 11. Edit log / audit trail — the non-negotiable

`project.md` §4.1; **MCA (Accounts) Rules, Rule 3(1)** as amended, in force **1 April 2023**, which
requires accounting software to have an audit trail recording every change with an edit log, that
cannot be disabled, and that is preserved.

### 11.1 What is captured

Every `Insert`, `Update` and `Delete` of **every entity mapped in `AppDbContext`** — masters and
vouchers alike — records: UTC timestamp (`DateTimeOffset`), user, machine name, action, entity type,
entity id, owning aggregate type and id (so a `JournalLine` change is findable from its voucher), the
list of changed property names, and the **full before and after images**.

There is deliberately **no opt-out**: no `[NotAudited]` attribute exists, no allow-list of audited
types exists, no configuration key exists. A new entity added in Phase 2 or Phase 7 is audited
because it is mapped, not because someone remembered to register it.

Full row images rather than property deltas on update, so the log is self-contained: any historical
state is readable from one row without replaying a chain. The cost is size; at this business's
volume that is the right trade.

### 11.2 Where it is captured

In `AppDbContext.SaveChangesAsync`, at the same interception point Phase 0 built for `IAuditStamped`
(`src/VinayakaAgencies.Domain/Common/IAuditStamped.cs`). The change tracker is walked *before*
`base.SaveChangesAsync`, and the log rows are inserted in the **same transaction** as the change they
describe: either both commit or neither does.

It is implemented as an override inside `AppDbContext` with a **required constructor dependency**, not
as an optional `IInterceptor` registered in DI — an interceptor can be left unregistered by a future
composition-root edit; a required constructor parameter cannot. Because it sits at
`SaveChangesAsync`, and because ADR-0001 keeps `DbContext` inside Infrastructure and routes all writes
through `IUnitOfWork`, there is no application code path that writes to the database without passing
through it. `ISqlQueryExecutor` is read-only by contract, so Dapper is not a bypass.

### 11.3 Serialisation

`System.Text.Json` with a fixed, canonical options set — the values are hashed (§11.5), so the
encoding must be stable across .NET versions and machine locales:

- scalar and shadow properties only; navigation properties are never serialised (each entity is
  logged by its own change-tracker entry, so a graph is never written twice);
- properties ordered ordinally by name;
- `decimal` written as an **invariant-culture string** with 4 decimal places (`"1234.5000"`) — a
  string, not a JSON number, so no reader can round-trip it through a double;
- `DateOnly` as `yyyy-MM-dd`; `DateTimeOffset` as round-trip `O` normalised to UTC; `Guid` as `D`;
- enums as their **string name**, never their integer value, so a later enum reordering cannot
  silently change the meaning of historical log rows;
- `null` as JSON `null`, never omitted.

Client-generated GUIDs (ADR-0001 §5) matter here too: entity ids are known *before* the insert, so
the log row can reference them without a second pass over the change tracker.

### 11.4 Why the log cannot be written to or removed

Layered so that no single mistake defeats it:

1. **`AuditLogEntry` does not derive from `Entity`.** `IUnitOfWork.Repository<TEntity>()` is
   constrained `where TEntity : Entity`, so `Repository<AuditLogEntry>()` does not compile. There is
   no generic repository through which a log row can be updated or removed. Its key is a monotonic
   `Sequence` (`INTEGER PRIMARY KEY AUTOINCREMENT`; `IDENTITY` from Phase 9), which the hash chain
   needs and which no other table uses.
2. **A guard in `SaveChangesAsync`** throws `AuditTrailTamperException` if any tracked entry of type
   `AuditLogEntry` is in state `Modified` or `Deleted`. This catches raw `DbContext` use from inside
   Infrastructure, which item 1 cannot.
3. **A database trigger.** The Phase 1 migration creates SQLite `BEFORE UPDATE` and `BEFORE DELETE`
   triggers on the log table that `RAISE(ABORT, …)`. Even a raw connection obtained from
   `IDbConnectionFactory` cannot modify or remove a row.
4. **No API surface.** The only way to read the log is `IEditLogQueryService`, which exposes queries
   and nothing else. There is no delete method, no purge command, no retention setting, no admin
   screen action, and no configuration key matching `audit` or `editlog` anywhere in `appsettings`.
5. **No permission gate.** Phase 1 has no permission system, and when Phase 9 introduces one, the
   audit trail must not become a permission-checked resource with a "manage" right. That is written
   into the Phase 9 spec's constraints, not left to memory.

### 11.5 Tamper evidence

Prevention stops at the file: through Phases 1–8 the database is a local SQLite file that a
determined user can open with any tool and, with enough effort, drop a trigger. So the log is also
made **tamper-evident**. Each row stores `PreviousHash` and `RowHash`, where `RowHash` is
SHA-256 over UTF-8 of the unit-separated (`U+001F`) concatenation of `PreviousHash`,
`OccurredAtUtc` (`O`, normalised to UTC), `UserName`, `MachineName`, `SaveBatchId`, `Action`
(**its name, not its integer** — §11.3's own reasoning applies here too), `EntityType`, `EntityId`,
`AggregateType`, `AggregateId`, `ChangedProperties`, `BeforeJson` and `AfterJson`, hex-encoded
lower-case. `Accounting.EditLog.ChainVerification` re-computes the chain and reports the first break.

> **Amended 15 Aug 2026 to the implemented field list**, after `architect-agent` review. This spec
> originally listed `Sequence` in the hashed payload and omitted `MachineName`, `SaveBatchId`,
> `AggregateType`, `AggregateId` and `ChangedProperties`. Hashing `Sequence` is **not implementable**:
> it is assigned by the database on insert, the row is sealed before `base.SaveChangesAsync`, and the
> `BEFORE UPDATE` trigger (§11.4 item 3) forbids re-sealing afterwards. The implementation in
> `AuditLogEntry.Seal` is correct and the spec is amended to match it — the wider field list covers
> strictly more of the row, so the guarantee is stronger, not weaker. A field separator that cannot
> occur in a GUID, an ISO-8601 timestamp or JSON text keeps two different rows from hashing
> identically by shifting content across a delimiter.
>
> One consequence for C1.16: the chain's `PreviousHash` linkage assumes rows are inserted in the
> order they were sealed, which EF Core does not formally guarantee within a batch. The C1.16 fixture
> must therefore include a single `SaveChanges` that writes three or more log rows, and assert the
> chain still verifies.

This does not prevent tampering; it makes tampering **provable**, which is what an auditor needs.
Real prevention arrives with SQL Server permissions at Phase 9 and data-at-rest protection at
Phase 10. Both are flagged in §13. If the owner judges the hash chain to be beyond Phase 1's scope,
say so at approval — but say so now, because retrofitting a chain over existing rows means either a
gap in the chain or a rebuild that itself looks like tampering.

### 11.6 User identity, honestly stated

Phase 1 has no login. `ICurrentUser` resolves to the Windows identity of the logged-in operator
(`EnvironmentCurrentUser`, built in Phase 0). On a single-user install that is a real, attributable
person, which satisfies Rule 3(1)'s "user" requirement in practice. It is not a named application
account, and Phase 9 replaces it with one. Recorded here so the limitation is on the record rather
than discovered during an audit.

## 12. Financial year, period locking and bank reconciliation

**Dates.** Voucher dates and bank dates are `DateOnly` — Indian financial-year logic depends on the
local calendar date, and a `DateTimeOffset` for a business date invites an off-by-one at midnight in
the wrong direction. Audit and system timestamps are `DateTimeOffset` in UTC. This is the `CLAUDE.md`
convention and it is applied without exception in this phase.

**Financial year.** Derived from `Company.FinancialYearStartMonth` (4, per D1): 1 April to 31 March.
`FinancialYear` rows are created lazily on the first post into a year, inside the posting transaction;
creation is a master change and is edit-logged like any other.

**Period locking.** `PeriodLock.LockedThroughDate` blocks new postings, alterations and cancellations
with a `VoucherDate` on or before it, and blocks a new revision from moving a date into the locked
range. Closing a `FinancialYear` (`IsClosed`) has the same effect for that year. Changing either is
itself an edit-logged master change — the lock is a control, not a hiding place.

**Bank reconciliation — manual only in this phase.** A `BankReconciliationMark` row records that one
`JournalLine` cleared the bank on a given `BankDate`. It is a separate table rather than a column on
`JournalLine`, because journal lines are insert-only (§8.2) — reconciliation state is operational
metadata, not financial content, so it may be added, changed and removed, and each of those is
edit-logged.

Known consequence, stated so it is not a surprise: a mark references a specific `JournalLine`, and
altering a voucher creates new lines, so the mark does not carry to the new revision and the item
reappears as unreconciled. That is the safe behaviour — an altered amount should be re-confirmed
against the bank — and the alteration is visible in the edit log. Statement import and auto-matching
are Phase 7 (`project.md` §10, Phase 7 — Banking).

## 13. Out of scope for Phase 1

Explicitly not in this phase. If a task appears to need one of these, stop and flag it
(`project.md` §9) rather than widening the phase.

| Not in Phase 1 | Belongs to |
|---|---|
| Stock, items, godowns, batches, serials, valuation, the stock ledger | Phase 2 |
| Quotation → order → delivery → invoice pipeline; FastReport templates and printing | Phase 3 |
| Customer master, customer categories, category pricing and margins, customer statements | Phase 4 |
| GST computation (CGST/SGST/IGST/cess), HSN/SAC, GSTR-1/3B/9, 2A/2B reconciliation, GST tax ledgers | Phase 5 |
| E-invoicing (IRN, signed QR), e-way bills | Phase 6 |
| TDS/TCS sections/rates/thresholds, payroll, cheque printing, bank statement import and auto-matching | Phase 7 |
| Tally XML/JSON import-export, Excel import-export (ClosedXML), column mapping | Phase 8 |
| SQL Server, multi-user, concurrency, logins, roles, permissions | Phase 9 |
| Backup/restore, data-at-rest protection, the performance/indexing pass, the installer | Phase 10 |

**Also out of scope, and specifically flagged because they look adjacent:**

- **Bill-wise details / bill references.** Outstanding receivables and payables need an allocation of a
  receipt against specific invoices. This is Phase 4's checkpoint, and it is deliberately *not*
  pre-built here — unlike cost centres (§9), bill allocation is a **separate child table hanging off
  `JournalLine`**, and adding a new table later is cheap, whereas adding a column to a large existing
  table is not. That asymmetry is the whole reason cost centres are in and bill-wise is out.
- **Reversal / amendment postings.** GST amendments in Phase 5 may need a contra-reversal rather than
  a revision. ADR-0002 chose revisions for Phase 1; if Phase 5 needs reversals as well, that is an
  additive decision and needs its own ADR.
- **Report export.** Phase 1 reports are on-screen only. Excel export is ClosedXML, which is a Phase 8
  dependency; PDF is FastReport, which is Phase 3. Do not add either NuGet package in this phase.
- **Numbering under concurrency.** §7.2's read-modify-write allocation is safe only because Phases 1–8
  are single-user. Phase 9 must replace it; noted here and to be carried into the Phase 9 spec.
- **Audit-log retention or archival.** There is no purge, and Phase 10's backup work must not
  introduce one. Any future archival must be copy-never-delete, to append-only storage, and needs an
  ADR.
- **SQL Server trigger interaction — resolved in Phase 1, not deferred.** EF Core 8 needs
  `.ToTable(t => t.HasTrigger(...))` for SQL Server tables carrying triggers, otherwise its
  `OUTPUT`-clause save path breaks. The audit-log table carries triggers on both providers, and
  `AuditLogEntry.Sequence` is store-generated, so the SQL Server provider will emit
  `INSERT … OUTPUT INSERTED.Sequence` — which SQL Server rejects on a table with enabled triggers.
  **Amended 15 Aug 2026:** the annotation is therefore declared **now**, in the Phase 1 audit
  configuration, naming both triggers. It is inert on SQLite, so it costs nothing today and Phase 9
  does not break. Still carried into the Phase 9 spec as a constraint, but no longer as an open task.
- **Migration rollback must never reach the UI (Phase 10 note).** `Down()` on the Phase 1 migration
  drops the audit-log table. That is inherent to how migrations work and is acceptable as a developer
  action, but Phase 10's backup/restore work must not surface migration rollback as a user-facing
  operation — that would be an audit-trail delete path wearing a maintenance label (§11.4). Carry
  this into the Phase 10 spec.

## 14. Checkpoint criteria

C1.1–C1.4 are from `project.md` §10 (Phase 1) and are copied verbatim. C1.5–C1.7 were added by the
Phase 1 stub. C1.8 onward are added here. Criteria may be extended, never weakened
(`specs/README.md` rule 4).

| # | Criterion | How it is verified |
|---|---|---|
| C1.1 | Posting a balanced voucher updates trial balance, balance sheet and P&L correctly | `PostVoucher_BalancedSalesVoucher_UpdatesTrialBalanceAndProfitAndLoss` — post a fixture voucher set with hand-computed expected figures, then assert each of the three statements line by line against them |
| C1.2 | An unbalanced voucher is rejected | `PostVoucher_DebitsDoNotEqualCredits_IsRejected` — including a case out by exactly one minor unit (₹0.0001), which must also be rejected; assert no `Voucher`, no `JournalLine` and no consumed number remain |
| C1.3 | Every create/alter/delete of a voucher or master appears in the audit trail with user + timestamp + before/after | `AuditTrail_CreateAlterCancel_WritesBeforeAndAfterImages` for a voucher, and the same for a ledger and a group; assert `UserName`, `OccurredAtUtc`, `Action`, and that the before/after JSON deserialises to the expected images |
| C1.4 | The audit trail cannot be turned off | Five tests, one per defence in §11.4: (a) a compile-time test asserting `typeof(AuditLogEntry).IsAssignableTo(typeof(Entity)) == false`, so `Repository<AuditLogEntry>()` cannot be instantiated; (b) mutating a log row through a raw `AppDbContext` throws `AuditTrailTamperException`; (c) `DELETE FROM AuditLogEntries` and `UPDATE AuditLogEntries` through `IDbConnectionFactory` are aborted by the trigger; (d) a reflection scan of the Application and App assemblies finds no member matching `Disable|Purge|Clear|Delete` on any audit type; (e) a configuration scan finds no `appsettings` key matching `audit` or `editlog` |
| C1.5 | Trial balance totals to zero for a randomised set of ≥500 generated vouchers | Generated fixture across several ledgers, dates and voucher types; assert `SUM(AmountMinor) == 0` exactly, on integer minor units, with no tolerance |
| C1.6 | Balance sheet balances: Assets == Liabilities + Equity + P&L for the same dataset | Same fixture; assert the identity exactly in minor units, with the P&L A/c figure taken from the balance-sheet query, not recomputed by the test |
| C1.7 | Day book for a date range matches the sum of its vouchers | Assert the day book's voucher set equals the current revisions in range, that cancelled vouchers appear and are flagged, and that superseded revisions do not appear |
| C1.8 | Numbering resets each financial year, blocks gaps, and logs manual overrides | Post across an FY boundary and assert the counter restarts; cancel a voucher and assert its number is not reissued; apply a manual override and assert the high-water mark advances, the edit log records it, and a duplicate override is rejected by the filtered unique index |
| C1.9 | The journal is append-only | Alter a posted voucher; assert the original `JournalLine` rows are byte-identical afterwards, a new revision exists, `SupersededByRevisionId` is set on the old revision, and a second attempt to set it throws |
| C1.10 | A closed period rejects postings | Set `LockedThroughDate`; assert a new voucher, an alteration and a cancellation in the locked range are all rejected, and that a revision cannot move a date into the locked range |
| C1.11 | No inline SQL outside the dialect layer; every query key resolves | Architecture test: no string literal containing `SELECT` in Application or App; and a test asserting every key in `AccountingQueryKeys` returns true from `ISqlDialect.CanResolve` for every registered dialect |
| C1.12 | No stored balance column exists | Schema test over the EF model: no mapped property on any master entity whose name matches `Balance|ClosingBal|Total` |
| C1.13 | Cost-centre schema ships with no UI | Migration test asserts `CostCentres`, `CostCategories` and the nullable `JournalLine.CostCentreId` exist; a source scan asserts no view or viewmodel references `CostCentre` |
| C1.14 | Materialised paths agree with `ParentGroupId` | Build a randomised group tree, re-parent nodes, then assert every `MaterialisedPath` equals the path recomputed from `ParentGroupId` alone |
| C1.15 | Money arithmetic is exact | Post amounts chosen to be inexact in binary floating point (e.g. 0.1 + 0.2 patterns, ₹1/3 splits); assert the trial balance is exactly zero and no `double`/`float` appears in any accounting type or report DTO |
| C1.16 | The audit hash chain verifies, and detects tampering | Verify the chain over the C1.5 fixture; then, with triggers dropped in a throwaway copy, mutate one row and assert verification reports that row as the first break |

## 15. Evidence required

`qa-agent` writes `/specs/evidence/phase-1-evidence.md` containing:

- full `build.ps1` output (build must be warning-free) and the `dotnet test` summary with per-test names;
- `dotnet ef migrations list` output and the generated DDL for the Phase 1 migration, so the reviewer
  can see for themselves that no balance column exists and that the audit triggers are created;
- the trial balance, P&L and balance sheet produced from the ≥500-voucher fixture, with the totals;
- the edit-log extract for one voucher taken through create → alter → cancel, showing the before/after
  images;
- the transcript of all five C1.4 tamper attempts and their rejections;
- the numbering-gap report for a series where a cancellation and a manual override both occurred;
- screenshots of the ledger master, voucher entry, day book, trial balance and edit-log viewer.

## 16. Sign-off — what must happen before `Status: Approved`

- [ ] **ADR-0003 (money persisted as scaled integer minor units under SQLite) is written and accepted.** §6. Implementation is blocked on this because it reinterprets a `CLAUDE.md` convention and touches every table that holds an amount.
- [ ] **Seed ledger list (§4.5) approved line by line** by the project owner — in particular L7–L9, which are marked "Owner decides".
- [ ] **Numbering series defaults confirmed** — the prefix and width for each of the eight voucher types (`open-questions.md` #14).
- [ ] **Hash chain (§11.5) confirmed in or out of Phase 1** — cheap now, awkward later.
- [ ] `open-questions.md` #7 resolved: rename the root namespace to match "Vinayaka Agencies", or keep `VinayakaAgencies`. Cheapest immediately before Phase 1 implementation starts; expensive after.
- [ ] Project owner has read §13 and agrees nothing in it is needed at launch.
- [ ] `Status` of this file changed to `Approved` **by the project owner**, and `CURRENT_PHASE` advanced to `1`.
