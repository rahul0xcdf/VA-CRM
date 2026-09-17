# Vinayaka Agencies — Accounting, Billing & Inventory for a Motor & Pump Trading Business

Windows-native (.NET 8 + WPF) accounting / billing / inventory suite, Tally- and Marg-class,
with India GST/TDS compliance as a core requirement.

**The master specification is [`project.md`](project.md). This file is the working contract; `project.md` is the source of truth.**

## Development status

- **Phase 0 — complete.** Solution, DI, logging, SQLite/EF Core, repository/UoW, migrations, shell.
- **Phase 1 (accounting core) — implemented and verified.** Chart of accounts masters, the journal
  engine (post/alter/cancel), numbering + gaps, derived reports (day book/TB/P&L/BS with drill-down),
  manual bank reconciliation, edit-log viewer with chain verification, all ten spec §2 screens.
  Remaining before Phase 1 sign-off: owner review of seed ledgers / numbering prefixes (§16 defaults),
  the ≥500-voucher stress evidence, and screenshots for `specs/evidence/phase-1-evidence.md`.
- **Phase 2 (inventory) — implemented and verified.** Spec approved 22 Aug 2026 with amendments
  recorded in §2a (simplified name/code item master per owner decision, FIFO default valuation,
  warn on negative stock). Stock groups / godowns / units / items / kits masters, the unified stock
  ledger (`StockLedgerEntry`, append-only), stock vouchers (opening/receipt/issue/transfer) posting
  atomically through the journal engine under an "Stock Journal" SJ/ series, serial lifecycle
  (C2.2/C2.3), FIFO + weighted-average valuation verified against hand-computed fixtures (C2.4),
  transfers preserving total value (C2.5), C2.1 reconciliation of stock value to the control
  account, reorder alerts + aging buckets, and the ≥5,000-SKU item lookup (C2.6, amended to
  name/code search). Quantities and values persist as scaled integer minor units throughout
  (ADR-0003 extended to stock); every model guard test still holds (no decimal-on-TEXT columns, no
  cascades, enums by name). Remaining before Phase 2 sign-off: owner walkthrough of the five
  Inventory screens and screenshots for `specs/evidence/phase-2-evidence.md`.
- **Phase 3 (sales & purchase pipeline) — implemented and verified by tests.** Spec approved
  22 Aug 2026 with decisions recorded in §4a (invoice-direct norm with data-only challans; one
  numbering series per company per document kind reusing §7 machinery; A4 full-page print with
  Rule 46 copy labels as template fields; FastReport deferred to ADR-0004 with templates as
  versioned data rendered by a built-in FlowDocument engine). Quotation → sales order → delivery
  challan → tax invoice and purchase order → goods receipt → purchase invoice, converting without
  re-keying (C3.1); invoices post journal voucher + mirror stock voucher + ledger rows + serial
  lifecycle in one transaction (C3.3); order outstanding reports track partial deliveries, and
  cancelling a posted invoice reopens a fulfilled order's balance (C3.4); quotations touch neither
  trial balance nor stock ledger (C3.5); two seeded tax-invoice templates resolve by lowest active
  priority (C3.6). Pipeline provisioning (`EnsurePipelineSeedAsync`) is idempotent at startup.
  Remaining before Phase 3 sign-off: owner walkthrough of the three Sales & Purchase screens and
  screenshots for `specs/evidence/phase-3-evidence.md`.
- **Phase 4 (customer & pricing engine) — implemented and verified by tests.** Spec approved
  23 Aug 2026 with decisions recorded in §4a (placeholder four categories seeded as master data;
  all three pricing bases per line — fixed price / margin on cost / margin on MRP; conversion
  holds quoted prices; per-customer special prices in scope, winning over the category list).
  Customers link 1:1 to auto-created Sundry Debtors ledgers so every Phase 1–3 posting path is
  unchanged; selecting a customer on a sale document auto-fills prices (C4.1) and overrides are
  proved structurally on the line (`AppliedRateMinor`/`IsPriceOverridden`/`PriceSource`), so the
  automatic audit trail shows original vs override without a manual audit writer (C4.2). All
  three bases verified against hand-computed fixtures (C4.4); the override gate is the
  `Sales.AllowPriceOverride` setting enforced in both UI and document service until Phase 9
  roles exist (C4.5); bill-wise outstanding allocates receipts oldest-bill-first and statements
  read straight from the books (C4.3/C4.6); credit limit warn/block gates orders and invoices.
  Remaining before Phase 4 sign-off: owner walkthrough of the five new screens and screenshots
  for `specs/evidence/phase-4-evidence.md`.
- **Phase 5 (GST returns & tax invoice compliance) — implemented and verified by tests.** Spec
  approved 23 Aug 2026; decisions in §4a (single GSTIN / one state, regular scheme with
  reverse-charge paths built though rare, domestic B2B + B2C only, GSTN schema pinned at
  **v3.2.4** and stamped on every export). HSN/SAC masters with effective-dated rate schedules
  (C5.5), posting-time tax snapshots on trade documents — CGST/SGST vs IGST by place of supply,
  cess, RCM self-assessment, rupee round-off posted to `Gst.RoundOffLedger` (C5.2); GSTR-1 with
  Table 12 B2B/B2C HSN split + JSON export pinned and stamped (C5.1), GSTR-3B reconciled to
  GSTR-1 and the books (C5.3), GSTR-9, GSTR-2B import reconciliation classifying matched /
  missing-in-books / missing-in-portal (C5.4). No rates/codes seeded anywhere — owner data only.
  The same batch delivered the Tally Prime-inspired presentation foundation per ADR-0005: shared
  theme dictionaries under `App/Presentation/Themes`, shell rework (header / numbered Home menu /
  status bar), Alt+G Go To palette, Esc back-stack, Ctrl+A/Ctrl+Q via `IScreenShortcuts`, digit
  module jumps, all existing views migrated onto the theme, four new GST screens built on it,
  and the shortcut map at `docs/user-guide/keyboard-shortcuts.md`. Feature docs:
  `gst-computation-and-rates.md`, `gst-returns-and-json-export.md`, `tally-style-ui-foundation.md`.
  Remaining before Phase 5 sign-off: owner walkthrough of the four GST screens + reworked shell,
  screenshots for `specs/evidence/phase-5-evidence.md`.
- **Phase 6 (e-invoicing & e-way bill) — implemented and verified by tests; three checkpoints blocked
  on credentials.** Spec approved 17 Sep 2026; decisions in §4a (direct to NIC, no GSP; turnover
  applicability as a data flag, never inferred; standard NIC Part-B field set; **sandbox credentials
  still not supplied** — the owner directed implementation to proceed regardless). Approval also
  corrected the draft's IRN field order: the hash is SHA-256 over `SupplierGSTIN + FY + DocType +
  DocNumber`, no separators, per NIC's published worked example — the draft's order produced a
  different hash, silently. IRN + signed-QR ten fields, registration lifecycle with a retryable
  `Pending` state, e-way bills by IRN with Part-B enforced per transport mode, validity slabs
  (1 day/200 km, and 1 day/20 km for over-dimensional cargo), a ₹50,000 threshold as a config row,
  DPAPI-encrypted credentials with two-pass redaction. **C6.4/C6.5/C6.6 met** — IRN against NIC's own
  example vector, retry-after-timeout producing no duplicate IRN (backed by a unique index on
  `(CompanyId, Irn)`), and a credential scan over persisted call rows and the whole SQLite file.
  **C6.1/C6.2 not met and C6.3 partial** — all three need the NIC sandbox (`open-questions.md` #10);
  the auth handshake, endpoint paths and payload field names are likewise unverified on the wire.
  Everything above the `IIrpClient` port is driven by `RecordedIrpServer` instead. §4b records the one
  schema addition beyond spec: structured address columns on `Company` and `Customer`, nullable,
  because the IRP needs an address a Phase 3 print-template header cannot yield. Feature docs:
  `einvoice-irn-and-qr.md`, `eway-bill.md`. Evidence filed at
  `specs/evidence/phase-6-evidence.md`. Remaining before sign-off: credentials, owner walkthrough of
  the three new screens, screenshots.
- **UI shell reworked to ADR-0006 (17 Sep 2026, owner-directed).** ADR-0005 delivered a themed
  sidebar with three shortcuts; the owner judged it unusable and the shell was rebuilt. Five bands
  (top menu / company bar / body / action bar / status line), a **Gateway** menu that is entered and
  escaped from rather than an always-on sidebar, accelerator letters highlighted inside each entry,
  and TallyPrime's published key map centralised in `Presentation/Keyboard/AppShortcut.cs` with a
  duplicate-gesture test. **`Ctrl+Q` now exits the screen and `Alt+X` cancels** (the old shell had
  `Ctrl+Q` cancelling). Screens contribute to the two bars through `IScreenShortcuts`; bars are
  generated from the commands they invoke, and reserved-but-unwired keys (F2/F3/F11/F12) are
  deliberately not shown. Also fixed 25 files of pre-existing double-encoded text that rendered as
  `Ledgers â€" grouped by...` on most screens. **Per-screen internals are NOT reworked yet** — dense
  entry grids, F4-F9 voucher switching, Alt+C create-on-the-fly and pop-up selectors are the next
  batch. See ADR-0006 and `docs/user-guide/keyboard-shortcuts.md`.
- **Feature docs live in `docs/features/<feature>.md`** — update them whenever you change a feature.
  Phase 2 additions: `inventory-masters.md`, `stock-journal-and-ledger.md`,
  `stock-valuation-and-reports.md`. Phase 3 additions: `sales-purchase-pipeline.md`,
  `print-templates.md`. Phase 4 additions: `customer-categories.md`, `customer-master.md`,
  `customer-pricing-engine.md`, `customer-statement-outstanding.md`.
  Phase 6 additions: `einvoice-irn-and-qr.md`, `eway-bill.md`.
  **End-user guides** (how to run the features from the screen) live in
  `docs/user-guide/` — Phase 3: `phase-3-sales-and-purchase.md`; Phase 4:
  `phase-4-customers-and-pricing.md`; Phase 6: `phase-6-einvoice-and-eway-bill.md`.

## Development workflow (replaces the removed Claude hooks)

The `.claude/hooks/*` gate scripts were **removed at the owner's instruction**; their rules survive
here as the working agreement. Every development session must:

1. Read `specs/CURRENT_PHASE`, then that phase's spec, then implement within its scope.
2. Verify as you go: build (`.\build.ps1 -NoTest`) and run relevant tests after each feature; fix
   what breaks before moving on. No feature counts as done while it fails or is untested.
3. Create/update one small workflow document per implemented feature under `docs/features/`
   (feature, user workflow, developer notes, verification).
4. Keep this file's status section current; do not duplicate instruction files.

## Non-negotiables

1. **Spec gate.** No code is written for Phase N until `/specs/phase-N-*.md` is marked `Status: Approved`.
   The enforcing `PreToolUse` hook was removed at the owner's instruction (the `.claude/hooks/`
   scripts are gone); the rule itself still stands and is honoured by the working agreement.
2. **One phase per batch.** Work only inside the current phase's scope (`/specs/CURRENT_PHASE`).
   If a task needs something from a later phase, **stop and flag it** — do not silently expand scope.
3. **Audit trail cannot be disabled.** Every create/alter/delete of a voucher or master is logged with
   user, timestamp, and before/after values. No user, including admin, may switch it off (MCA Rule 3(1)).
4. **Ledger balances are always derived**, never stored as an independently-mutable column.
5. **All DB access goes through the repository / unit-of-work abstraction** — never a bare `DbContext`
   outside `VinayakaAgencies.Infrastructure`. Phase 9 migrates SQLite → SQL Server Express; that must not be a rewrite.
6. **Nothing tax-related is hardcoded.** HSN/SAC codes, GST rates, TDS/TCS sections, rates and thresholds
   are data-driven config rows.
7. **Evidence, not assertion.** A phase is done when verification output exists (test runs, smoke
   transcripts, screenshots filed under `specs/evidence/`) — not when an agent says "done".

## Tech stack

| Layer | Choice |
|---|---|
| Runtime / UI | .NET 8, C# 12, WPF, MVVM (CommunityToolkit.Mvvm) |
| DB (Phase 1–8) | SQLite (single file) |
| DB (Phase 9+) | SQL Server Express |
| Writes / migrations | EF Core 8 |
| Reporting queries | Dapper (via `ISqlQueryExecutor`) |
| Reports / printing | FastReport .NET WPF (runtime designer — end users edit templates) |
| Excel | ClosedXML (MIT). **Never EPPlus** — paid licence since v5. |
| Tally interop | Tally XML "Data Interchange" (file) → later XML-over-HTTP, port 9000. **Never TallyODBC.** |
| Logging | Serilog (console + rolling file under `%LOCALAPPDATA%\VinayakaAgencies\logs`) |
| Tests | xUnit + **Shouldly**. **Never FluentAssertions** — paid licence since v8. |

## Layout

```
src/VinayakaAgencies.Domain          entities + abstractions, zero external deps
src/VinayakaAgencies.Application     services, DTOs, use-cases; depends on Domain only
src/VinayakaAgencies.Infrastructure  EF Core, DbContext, repositories, UoW, Dapper, migrations
src/VinayakaAgencies.App             WPF host: DI, Serilog, navigation shell, views/viewmodels
tests/…                            mirrors src
specs/                             one file per phase + ADRs + evidence
docs/features/                     one small workflow doc per implemented feature
.claude/agents/                    legacy subagent definitions (workflow now runs without them)
.claude/worktrees/                 stale Claude Code worktrees (do not use)
```

**Dependency rule:** `Domain ← Application ← Infrastructure ← App`. Domain references nothing.
Application never references EF Core. Only Infrastructure knows the database exists.

## Commands

```powershell
.\build.ps1                 # restore + build + test  (run before ending any session)
.\build.ps1 -NoTest         # restore + build only
dotnet build VinayakaAgencies.sln -c Debug
dotnet test  VinayakaAgencies.sln
dotnet run --project src\VinayakaAgencies.App
```

EF Core migrations (always target the Infrastructure project, start from the App project):

```powershell
dotnet ef migrations add <Name> -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App -o Persistence\Migrations
dotnet ef database update  -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App
```

## Conventions

- **Nullable enabled, warnings as errors.** Treat a warning as a build break; do not suppress without an ADR.
- **Money is `decimal(18,4)`; quantity is `decimal(18,4)`.** Never `double`/`float` for either.
- **Dates:** store UTC (`DateTimeOffset`) for audit/system timestamps; store the business date
  (voucher date, invoice date) as a `DateOnly` — Indian financial-year logic depends on the local calendar date.
- **Entities** derive from `Entity` (GUID `Id`, generated client-side so Tally export GUIDs are stable).
- **Async all the way** for I/O; suffix `Async`; pass `CancellationToken` as the last parameter.
- **One public type per file**, file name == type name.
- **No `#region`.** No commented-out code. No `TODO` without a spec reference (`// TODO(phase-3): …`).
- Test naming: `Method_Scenario_ExpectedResult`.
- ViewModels are `ObservableObject`; no code-behind logic beyond `InitializeComponent()`.
- **Reporting rows carry integer minor units** (`long`) and are converted to `decimal` once, in the
  report-service mappers — never in SQL, never via floating point (ADR-0003).
- **Dapper cannot bind `DateOnly`.** Render business-date parameters with `CalendarText.For(...)`
  (`yyyy-MM-dd` text), which is exactly how EF Core stores them.
- **Child entities of an aggregate implement `IBelongsToAggregate`** so edit-log rows group under
  their aggregate root; a child whose audit grouping needs its root's id carries it denormalised
  (see `JournalLine.VoucherId`).

## Working agreement

- Read `/specs/CURRENT_PHASE`, then that phase's spec, before touching anything.
- Prefer editing an existing file over creating a new one; prefer extending a spec over inventing behaviour.
- Schema or public-contract changes: keep them minimal and additive within the phase's spec, and
  record the reason in the spec file or an ADR (the `architect-agent` subagents are no longer part
  of the workflow; their review duty falls to the development agent and the owner).
- End of every phase: file `specs/evidence/phase-N-evidence.md` (test output + smoke transcript +
  screenshots) for the owner's review.
