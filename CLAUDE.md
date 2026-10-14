# Vinayaka Agencies — Accounting, Billing & Inventory for a Motor & Pump Trading Business

Windows-native (.NET 8 + WPF) accounting / billing / inventory suite, Tally- and Marg-class,
with India GST/TDS compliance as a core requirement.

**The master specification is [`project.md`](project.md). This file is the working contract; `project.md` is the source of truth.**

## Development status

- **Phase 0 — complete.** Solution, DI, logging, SQLite/EF Core, repository/UoW, migrations, shell.
- **Phase 1 (accounting core) — implemented; evidence filed 5 Oct 2026 at
  `specs/evidence/phase-1-evidence.md`.** Chart of accounts masters, the journal engine
  (post/alter/cancel), numbering + gaps, derived reports (day book/TB/P&L/BS with drill-down),
  manual bank reconciliation, edit-log viewer with chain verification, all ten spec §2 screens.
  **The ≥500-voucher stress evidence is done** — `RandomisedLedgerFixture` posts 523 randomised
  vouchers across 15 ledgers of five natures, four voucher types and every date in FY 2026-27
  through `IVoucherPostingService`, carrying C1.5/C1.6/C1.7/C1.16/C1.19; set `VA_EVIDENCE_DIR` and
  the run writes the three statements out. **All twenty criteria are now met.** Eight were short of
  a test §14 names and nobody wrote: C1.4's defences (d)/(e)
  (`AuditTrailCannotBeDisabledGuardTests` — non-negotiable #3 is now guarded by the build), C1.11
  (the `AccountingQueryKeys.All` dialect walk its own comment had been asking for since Phase 1,
  plus an upper-case-`SELECT` scan), C1.13, C1.15 (by reflection, so "double-counts" in a comment
  cannot trip it), C1.16 (triggers dropped, a middle row rewritten, verification naming it as the
  first break), C1.18, C1.19 and C1.14. **C1.18** needed `AddInfrastructure` to pass registered
  `IInterceptor`s to the `DbContext` — a no-op in the product, which registers none; the audit
  trail deliberately still does **not** use that seam, staying in `SaveChangesAsync` with required
  constructor parameters so it cannot be unregistered.
  **Five pre-existing defects** (evidence §6), three of them found only by assembling the real
  thing. Beyond the balance-sheet / Contra / bank-ledger trio: **C1.14 could not be written because
  no service re-parented a group at all** — `MoveTo` and `SetMaterialisedPath` each documented that
  the caller must rebuild the subtree's paths, and there was no caller, so the first one added
  later would have inherited a roll-up-corrupting trap; `MoveGroupAsync` now discharges it (refuses
  a destination inside the subtree, a system group, a name clash, excess depth, and a move across
  natures). And **the Ledgers screen could never show a ledger**: `RefreshCommand` had no XAML
  binding, no shortcut-bar entry and no internal caller — zero references — so the list was
  unreachable while the empty state blamed a filter the operator had not set. Fixed with a
  *Refresh* control; `ScreenLoadReachabilityTests` is the guard.
  **Three pre-existing Phase 1 defects were found by that fixture and fixed** (evidence §6):
  the balance sheet negated every liability **twice** — once in `BalanceSheetAsOf`, once in
  `MapBalanceSheetRow` — so liability lines showed inverted and `IsBalanced` could only be true when
  both sides were zero, which is exactly what the one existing test produced; the Contra rule and
  `ListBankLedgersAsync` both matched display-cased group names against the upper-cased
  `NameNormalised` column case-sensitively, so **every Contra voucher was refused** and the bank
  reconciliation screen's account picker was **always empty**. Sign normalisation now happens only
  in the mapper — the Phase 9 dialect must keep SQL free of it.
  Remaining before Phase 1 sign-off: owner review of seed ledgers / numbering prefixes (§16
  defaults), screenshots, the two §15 artefacts still missing (a voucher's create→alter→cancel
  edit-log extract, and the numbering-gap report after a manual override), and open-questions.md
  #17.
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
  cascades, enums by name). **Evidence filed 5 Oct 2026 at `specs/evidence/phase-2-evidence.md`;
  all six criteria met.** One scope note recorded there: C2.1's reconciliation is asserted after a
  receipt, an issue and a transfer, not over a multi-year book — nothing suggests it would fail, it
  simply has not been run at that volume. Remaining before Phase 2 sign-off: owner walkthrough of
  the five Inventory screens and screenshots.
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
  **Evidence filed 5 Oct 2026 at `specs/evidence/phase-5-evidence.md`: C5.2–C5.5 met, C5.1
  partially met.** C5.1 asks the GSTR-1 JSON to *validate against the current GSTN schema*; the
  schema sits behind the authenticated GSTN developer portal and is **not in the repository** —
  tracked as **open-questions.md #23**, same shape as #10 and #22. Pursuing it was worth it:
  reviewing the export against two independent published references to the GSTN payload found
  **three real encoding defects** — cess written as `csft` instead of `csamt` (**every cess amount
  would have filed as nil**), the HSN code as `hsn` instead of `hsn_sc`, and a B2B invoice's rate
  figures flat on `itms` instead of nested under `num` + `itm_det`. All fixed. **Four divergences
  are known and deliberately not guessed at** (#23): the B2C section shape (the portal's is a flat
  `b2cs`, not our nested `b2c`), the absent `b2cl` section, the `gtotals` block (portal: `gt` /
  `cur_gt`) and the `uqc` hardcoded to `NOS` — these change *what* is reported, so they need the
  schema and the owner's CA. The emitted shape is now **locked by a real JSON Schema evaluator**
  (`JsonSchema.Net`, MIT) against `Gst/Schemas/gstr1-as-built.schema.json`, with
  `Gstr1SchemaValidationTests` proving the schema bites so the validation cannot become theatre;
  drop the real schema in that folder as `gstr1-gstn.schema.json` to close C5.1.
  **GSTR-9 was shipped with no test at all and is now covered** — and writing it found a fourth
  defect: `BuildGstr9Async` stepped its month window from `fromDate`, so any period not starting on
  the 1st left a hole and **silently dropped the invoices in it** (₹1,200 reported as ₹500). Not
  reachable from the screen, which passes the FY start, but live on a public service method whose
  output is filed. Fixed; the windows now tile the period exactly.
  The ADR-0005 shell this spec describes was superseded by ADR-0006/0007, so walk the GST screens
  on the current shell.
  Remaining before Phase 5 sign-off: the GSTN schema / offline-utility dry run (#23), owner
  walkthrough of the four GST screens, and screenshots.
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
  `Ledgers â€" grouped by...` on most screens. See ADR-0006 and
  `docs/user-guide/keyboard-shortcuts.md`.
- **Per-screen internals reworked to ADR-0007 (18 Sep 2026, owner-directed).** ADR-0006 deferred the
  inside of every screen; this closes it, and the ADR is **binding on every screen built from Phase 7
  onward**. `LookupBox` replaces every master-picking `ComboBox` (filters on each keystroke, matches
  anywhere in the name, prefix matches ranked first, `Alt+C` creates the missing master into a band at
  the foot of the page and drops it straight onto the line being filled). Voucher lines are an
  `ItemsControl` of permanently live fields driven by the `EntryGrid` behaviour — `Enter` walks across
  the line and appends one at the end — not an editable `DataGrid`. `F4`-`F9` / `Alt+F5`-`F9` /
  `Ctrl+F8`-`F9` switch voucher type, document kind and stock purpose through `VoucherTypeKeyMap`,
  keeping typed lines. Reports drill on `Ctrl+Enter` (trial balance, P&L, balance sheet, day book,
  edit log) into a pane that stays collapsed until asked for; the trial balance's old
  drill-on-selection ran a statement query per arrow key and is gone. Master screens share one key
  set: `Alt+C` new, `Ctrl+A` save, `Alt+D` delete-or-deactivate, `Alt+X` reload the selected record.
  **Two keys changed meaning on voucher entry: `Alt+X` now clears the form and `Alt+D` cancels the
  posted voucher** (`Alt+X` used to cancel the posted voucher). Three pre-existing defects fixed on
  the way: `ShellViewModel` never subscribed to the active screen, so the bars — which are the shell's
  key routing table — were frozen at construction; stock groups and godowns never loaded the selected
  row into their editor, so Save renamed whatever the editor still held. Feature doc:
  `tally-entry-patterns.md`. Remaining before sign-off: owner walkthrough and screenshots.
- **Phase 7 (TDS/TCS, payroll, banking) — implemented and verified by tests.** Spec approved
  18 Sep 2026; decisions in §4a (194C only at launch and nothing seeded; 194Q and 206C(1H) both
  built and both gated by a declared flag, never inferred from turnover; payroll scoped to employee
  master, pay heads, salary voucher and PF/ESI/PT as effective-dated config behind per-statute
  flags, with Form 24Q and Form 16 deferred; cheque layouts reusing the Phase 3 print-template
  engine with no bank seeded; delivered as 7a TDS+banking then 7b payroll).
  **All eight checkpoints met** — C7.1 deduction computed from config rows and posted as balanced
  journal lines; C7.2 a section usable from two data rows with no rebuild; C7.3 the annual
  threshold's catch-up on the crossing payment; C7.4 dated rate changes on either side of the
  effective date; **C7.5 executed as a test** (`NoHardcodedTaxFigureGuardTests`) rather than a grep
  somebody remembers — no section number in any string or identifier in `src/`, and `TdsMath` /
  `PayrollMath` hold no figure beyond the minor-unit scale; C7.6 the 194Q/206C(1H) exclusion driven
  by the buyer's recorded declaration; C7.7 every statutory flag off producing no row and running no
  statutory path; C7.8 a cheque layout repositioned as template data — the software half proven, the
  physical print pending the owner. Section 197 certificates (nil, capped, expired) and the 206AA
  no-PAN uplift are covered. Nothing tax-related is seeded anywhere — not even 194C.
  §4b records three additive changes beyond spec: `PrintTemplate.DocumentKind` retyped to the new
  `PrintDocumentKind` so a cheque is not forced into the trade-document enum (names unchanged, so no
  data migration), positioned `Fields` on `PrintTemplateLayout`, and
  `TaxPayeeProfile.DeclaresDeductingOnPurchases` which C7.6 needs.
  **Two pre-existing Phase 5 defects were found and fixed on the way**: `GstRateService.AddAsync`
  rejected every rate change (an open-ended row covers every later date, so the overlap guard made
  supersession impossible and the close-the-previous-row branch unreachable) — the Phase 7 TDS
  service inherited it and C7.4 caught it, both are fixed and `GstRateScheduleTests` is the
  regression; and `EnsureGstSeedAsync` was never called from startup, so an install that had not
  opened a GST screen had no tax ledgers to post to. Feature docs: `tds-tcs-engine.md`,
  `banking-and-cheque-printing.md`, `payroll-statutory-basics.md`. Evidence filed at
  `specs/evidence/phase-7-evidence.md`. Remaining before sign-off: owner walkthrough of the seven
  new screens, screenshots, the physical cheque print, and the payroll headcount (§5 — it gates
  nothing built and only decides whether the PF/ESI flags are on at go-live).
- **Phase 8 (Tally & Excel interoperability) — implemented and verified by tests; C8.1 blocked on a
  real Tally.** Spec approved 24 Sep 2026; decisions in §4a (TallyPrime **7.0+**, so XML *and* JSON as
  two serialisers over one canonical model; a **full history is being migrated in**, so import is a
  first-class half of the phase and a real Tally export is a tracked dependency —
  `open-questions.md` #22 — that blocks C8.1/C8.6 sign-off but not implementation; both directions
  built off one mapping layer that Excel's column-mapping step also targets; cost-centre name
  uniqueness (#18) now live because Tally import matches cost centres by name; **Marg out of scope**).
  `InteropDocument` is the vendor-neutral model both directions read, everything in minor units;
  `TallyConventions` holds every encoding rule the format imposes — the amount sign above all, since
  Tally writes a debit as a **negative**, and a round trip cannot catch that being wrong. JSON is
  transcoded from the identical element tree, so a field cannot exist in one format and not the other.
  Import is two passes — validate everything, then write in one transaction — posting through
  `IVoucherPostingService` and nowhere else. **C8.2–C8.5 met; C8.6 met against our own export**
  (a second SQLite database reproducing the origin's trial balance and closing stock). **C8.1 not met**
  and C8.6 unproven against real Tally data — both need #22.
  §4b records four things beyond spec: **voucher types exported as masters** (a company's own types —
  "Stock Journal", "Cash Sales" — are not names Tally ships with, and inferring a posting behaviour
  from an unrecognised name would put real money down the wrong path); `Interop.JournalTotals`,
  because the trial balance is financial-year scoped and a multi-year migration would reconcile
  against one year; the `ExcelColumnMapping` schema (one migration, the only rows the phase persists);
  and **a pre-existing Phase 2 defect fixed** — `ReconcileValuationAsync` threw on a company with no
  stock movements, which would have surfaced as the Stock Valuation screen crashing on a new install.
  Three screens under a new **Import & Export** group. Feature docs: `tally-interchange.md`,
  `excel-import-export.md`. Evidence at `specs/evidence/phase-8-evidence.md`. Remaining before
  sign-off: the real Tally export file (#22), the exchange with the accountant, owner walkthrough of
  the three screens, and screenshots.
- **Phase 9 (multi-user / LAN) — four of five checkpoints met; the SQL Server provider was
  deliberately not written.** Spec approved 9 Oct 2026; decisions in §4a (**2–3 counters**;
  **application accounts, not Windows auth**, because non-negotiable #3 needs a per-person name and
  a workgroup shop shares one Windows login per machine; SQL Server Express on the **main billing
  PC**, which makes whoever runs it the backup custodian; **Owner and Operator only**, as data rows,
  superseding Phase 4's `Sales.AllowPriceOverride` stand-in). `open-questions.md` #4 closed by
  circumstance — Phases 1–8 shipped single-user, so the pull-forward it guarded never came due.
  **C9.2 and C9.5 met**: `PermissionKind` grants held by `AppRole` rows, checked by
  `IPermissionAuthority` in the services — voucher post/alter/cancel, every `IUserAdminService`
  member, and the price override (which now needs the Sales setting **and** the permission, so an
  upgrade is behaviour-identical). C9.5's test calls `CancelAsync` straight off the container with a
  voucher id that does not exist, so denial-before-work is what is proved. **C9.1 and C9.3
  partially met** — the mechanisms are proved and the "two SQL Server clients" wording is not
  (#24): a `ConcurrencyToken` on every `IConcurrencyStamped` entity surfaces a lost update as
  `ConcurrentUpdateException` with the first write intact, and `IVoucherNumberSequenceLock` reads the
  high-water mark under a row lock held by the voucher's own transaction. **C9.4 not met.**
  **`IPermissionAuthority` grants everything while a company has no accounts** — the bootstrap state,
  which has to exist (the first account could not otherwise be created) and is why the Phase 1–8
  suites are untouched. **The product can no longer sit in it** (§4b B7): first-run creates the owner
  account alongside the company, validating the account fields before the company so a mistyped
  password leaves no books behind, and an upgraded install cannot open the shell without a session.
  Startup now **gates the schema upgrade**
  (`ApplyMigrationsOnStartup` defaults off) and **asks for a sign-in** before opening the books;
  Users and Roles are two new screens under an Administration group.
  §4b records seven changes beyond spec, §4c records the stop. **C9.4 was stopped rather than
  half-built**: there is no SQL Server, LocalDB or Docker on this machine (#24), and a second
  provider's migrations need their own project — a solution-level change for the owner to approve.
  What was delivered instead answers §2's own question, and `SqlServerPortabilityTests` keeps the
  answer true: the SQLite-only surface is **eight occurrences across four construct kinds**
  (`||`, `LIMIT`, `substr()`, `length()`) in ~700 lines of reporting SQL, with none of the
  constructs lacking a mechanical T-SQL equivalent present. **ADR-0001's dialect layer held — this
  is a provider swap, not a rewrite, and there is no Phase 0 defect to record.**
  **Four defects found and fixed**: the concurrency token written as an EF shadow property broke
  every `AsNoTracking`-then-`Update` service (caught by `GstRateScheduleTests`, the test Phase 7
  added — its second save); a `Cascade` on role grants (caught by
  `Model_ConfiguresEveryRelationshipAsRestrict`, which is stated "without exception" and was right);
  and **two startup defects that 1,117 green tests could not see** — a `BorderBrush` the theme does
  not define and a stray `/>`, which killed the product at launch because
  `ScreenViewLoadTests` sweeps the module catalogue and **no module lists a startup window**.
  `EveryWindowInTheApplication_LoadsWithTheApplicationResources` now constructs every one.
  Feature docs: `users-roles-and-permissions.md`, `concurrency-and-schema-upgrades.md`. User guide:
  `phase-9-users-and-upgrades.md`. Evidence at `specs/evidence/phase-9-evidence.md`.
  Remaining before sign-off: a SQL Server instance (#24), owner walkthrough of the four new windows
  and screenshots, owner confirmation of the Operator grant list and the eight-character password
  floor.
- **Phase 10 (hardening & packaging) — three of six checkpoints met; the other three need a machine
  this repository does not have.** Spec approved 14 Oct 2026 on the owner's instruction, with the
  installer named as the thing they wanted first; decisions in §4a (backups to local disk with an
  optional second destination, **30** retained and **never the last one**; **no cloud backup** —
  `project.md` §1 keeps it out of v1; response budgets 300 ms for the billing item lookup and
  1.5–2 s for the reports; **unsigned installer** with the signing step wired, because an OV
  certificate does not remove the day-one SmartScreen warning either; **the database deliberately
  not encrypted**, with the recovery reasoning written out and BitLocker recommended instead;
  **MSI via WiX v5 carrying a self-contained publish**, so a fresh machine needs no .NET runtime and
  no internet; and **company data moved to `%ProgramData%`**).
  **C10.1, C10.5 and C10.6 met. C10.2 not met; C10.3/C10.4 partially met** — installing, upgrading
  and uninstalling a per-machine MSI are actions on a computer, and the MSI's structure is verified
  instead (`installer/verify-installer.ps1` asserts nineteen facts about the built package without
  installing it). **Nothing in this phase adds a migration** (§4b B0).
  **A pre-existing Phase 0 data-loss defect was found and fixed** (§4b-1): the database sat under
  `%LOCALAPPDATA%`, which is **per Windows user**, so a counter PC with two Windows accounts would
  have kept **two separate sets of books** — and neither would have looked broken, because the second
  user would simply have been walked through first-run company creation. The per-machine install this
  phase adds is what makes it reachable. Company data now lives machine-wide with an explicit `Users`
  ACL, and `LegacyDataRelocator` moves an existing install's books across on first launch, refusing
  to merge when it finds a database in both places.
  **C10.6's fingerprint had to be redesigned**: the trial balance is financial-year scoped, so the
  obvious "as on a far-future date" reading yields an *empty* statement — a fingerprint that would
  match itself after any restore whatsoever and carry no information. It now renders one trial
  balance per financial year on record. **Two defects in this phase's own code were caught by its own
  tests**: a pooled SQLite handle kept the snapshot file locked, and `PRAGMA integrity_check` threw
  on a corrupt archive instead of reporting it (a verifier that throws is not a verifier).
  Backups are verified ZIPs carrying the database snapshot (SQLite's online backup API — a file copy
  of a live WAL database is not a backup), the logo and local settings; print templates are rows, so
  the snapshot already holds them. Restore verifies first, refuses an archive from a newer schema,
  takes a safety copy before replacing, and is armed-then-confirmed on screen. The Phase 6 C6.6
  credential scan now covers backup archives and diagnostics bundles, and the bundle resolves the
  stored secrets so Phase 6's **value** pass runs too. Three unhandled-fault handlers where Phases
  0–9 had one. Feature docs: `backup-and-restore.md`, `installer-and-packaging.md`,
  `diagnostics-and-crash-reporting.md`. User guide: `phase-10-install-and-backup.md`. Evidence at
  `specs/evidence/phase-10-evidence.md`.
  Remaining before sign-off: a fresh Windows machine for C10.2/C10.3/C10.4, the two-Windows-account
  check that proves the D7 fix, owner walkthrough of the Backup & Restore screen and screenshots, and
  owner confirmation of the retention count, the response budgets and the decision not to encrypt the
  database. **Note for the owner's "other device" request:** this phase delivers a standalone install
  plus a verified way to move books between machines. Sharing *one* set of books between two counters
  is Phase 9's C9.4, deliberately stopped (#24) — a separate conversation.
- **Feature docs live in `docs/features/<feature>.md`** — update them whenever you change a feature.
  Phase 2 additions: `inventory-masters.md`, `stock-journal-and-ledger.md`,
  `stock-valuation-and-reports.md`. Phase 3 additions: `sales-purchase-pipeline.md`,
  `print-templates.md`. Phase 4 additions: `customer-categories.md`, `customer-master.md`,
  `customer-pricing-engine.md`, `customer-statement-outstanding.md`.
  Phase 6 additions: `einvoice-irn-and-qr.md`, `eway-bill.md`.
  Phase 7 additions: `tds-tcs-engine.md`, `banking-and-cheque-printing.md`,
  `payroll-statutory-basics.md`. Phase 8 additions: `tally-interchange.md`,
  `excel-import-export.md`. Phase 9 additions: `users-roles-and-permissions.md`,
  `concurrency-and-schema-upgrades.md`. Phase 10 additions: `backup-and-restore.md`,
  `installer-and-packaging.md`, `diagnostics-and-crash-reporting.md`.
  UI: `tally-style-ui-foundation.md` (shell, ADR-0005/0006) and `tally-entry-patterns.md`
  (inside the screens, ADR-0007 — read this before building any new screen).
  **End-user guides** (how to run the features from the screen) live in
  `docs/user-guide/` — Phase 3: `phase-3-sales-and-purchase.md`; Phase 4:
  `phase-4-customers-and-pricing.md`; Phase 6: `phase-6-einvoice-and-eway-bill.md`;
  Phase 7: `phase-7-tds-payroll-and-banking.md`; Phase 8: `phase-8-interoperability.md`;
  Phase 9: `phase-9-users-and-upgrades.md`; Phase 10: `phase-10-install-and-backup.md`.

## Development workflow (replaces the removed Claude hooks)

The `.claude/hooks/*` gate scripts were **removed at the owner's instruction**; their rules survive
here as the working agreement. Every development session must:

1. Read `specs/CURRENT_PHASE`, then that phase's spec, then implement within its scope.
2. Verify as you go: build (`.\build.ps1 -NoTest`) and run relevant tests after each feature; fix
   what breaks before moving on. No feature counts as done while it fails or is untested.
3. Create/update one small workflow document per implemented feature under `docs/features/`
   (feature, user workflow, developer notes, verification).
4. Keep this file's status section current; do not duplicate instruction files.
5. **Run the product, not only the suite.** Three of the defects found on 5 Oct 2026 were invisible
   to 1,000+ passing tests because the code was correct and *unreachable* — a service wired into
   nothing, a domain contract no caller honoured, a command no control bound.
   `EveryScreenAgainstARealDatabaseTests` resolves and loads all 41 screens against a real migrated
   database, and `ScreenLoadReachabilityTests` asserts each screen has some path to its own load;
   both exist because unit tests reach code directly and so never ask whether anything else does.

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
- **Every new screen follows ADR-0007.** It is not a suggestion from a past batch — the owner made it
  the standard for the phases that follow. In short: a field that picks a master is a `LookupBox`,
  never a `ComboBox`; a grid of enterable lines is an `ItemsControl` with `EntryGrid.Enable`, never an
  editable `DataGrid`; gestures come from `AppShortcuts` and are bound only through the screen's own
  bars; a key is declared only where it works; and any behaviour with a rule puts that rule somewhere
  a unit test can reach without a window. The per-shape key tables are in the ADR and in
  `docs/features/tally-entry-patterns.md`.
- Schema or public-contract changes: keep them minimal and additive within the phase's spec, and
  record the reason in the spec file or an ADR (the `architect-agent` subagents are no longer part
  of the workflow; their review duty falls to the development agent and the owner).
- End of every phase: file `specs/evidence/phase-N-evidence.md` (test output + smoke transcript +
  screenshots) for the owner's review.
