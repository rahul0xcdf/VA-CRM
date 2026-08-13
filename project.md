# MASTER.md — Motor & Pump Trading Business: Accounting, Billing & Inventory Suite
### (Tally/Marg-class, Windows-native, built in phases with Claude Code)

This is the single master specification for an AI coding agent (Claude Code) to build a Windows-native business accounting, billing, and inventory management application. Give this file to Claude Code as the starting point for the project.

---

## 0. How to Use This Document
- Read this entire document before starting Phase 0.
- **Phase 0's first job is to convert Sections 6–9 of this document into the actual Claude Code project scaffolding**: `CLAUDE.md`, `.claude/agents/*.md`, `.claude/settings.json` (hooks), and `/specs/phase-*.md` files.
- Work proceeds **one phase at a time, in separate sessions ("batches")**. Do not start Phase N+1 until Phase N's checkpoint (Section 10) has been reviewed and explicitly approved by the project owner.
- Section 11 lists assumptions made while writing this spec — confirm or correct them before Phase 1 begins.

---

## 1. Project Overview
- **What we're building:** A Windows-native (.NET 8 + WPF) desktop application for business accounting, billing, and inventory management — functionally comparable to TallyPrime and Marg ERP — tailored to a motor & pump distribution/dealership business, with India GST/TDS compliance as a core, non-optional requirement.
- **Primary user:** A motor & pump dealer/distributor with a large, technically detailed item catalog (many SKUs across brands and models).
- **Inspiration, not clone:** Match TallyPrime/Marg on core accounting, inventory, billing and GST workflows. Do not copy their UI or code — build an original .NET/WPF product with a data model designed for this specific business.
- **Out of scope for v1** (revisit after Phase 10): payroll beyond basic statutory outputs, deep manufacturing/BOM, mobile apps, cloud/remote access, multi-currency.

---

## 2. Business Domain Notes — Motor & Pump Vertical
> **Assumption flagged for confirmation:** exact item attributes weren't specified, so Phase 1/2 should start with the list below and be adjusted before Phase 2 is marked complete.

- **Extended item master fields:** Brand/Make, Model No., HP/KW rating, Phase (1-Ph/3-Ph), Voltage/Frequency, Pump/Motor type (submersible, centrifugal, monoblock, openwell, etc.), Discharge/Head specs, Warranty period (months), HSN/SAC code kept as a **per-item configurable field, not hardcoded** (rates and codes change).
- **Serial number tracking per unit sold** — most motors/pumps carry an individual serial used for warranty claims. This is a native Tally/Marg capability (batch & serial tracking); turn it on for this item category.
- **Unit of measure with alternate/compound units** where sourcing and selling units differ.
- Because of the high SKU count, item search must support brand/model/HP filters, not just name/code lookup, and barcode support should exist even for items that don't currently carry a barcode (Marg-style barcode-free billing).

---

## 3. Locked Technical Decisions

| Area | Decision | Why |
|---|---|---|
| Language / UI | .NET 8, C#, WPF, MVVM | Native Windows, mature ecosystem, works well with Claude Code |
| Database (Phase 1–8) | SQLite, embedded, single file | Zero-install, simple single-user deployment |
| Database (Phase 9+) | Migrate to SQL Server Express | Free, same T-SQL family, built for concurrent multi-user access (unlike SQLite/LocalDB) |
| Data access | EF Core (writes/migrations/domain) + Dapper (reporting queries) | Standard hybrid pattern for transactional + reporting-heavy apps |
| Reporting / printing | **FastReport .NET WPF** | Ships a **runtime designer** your own staff can use to edit invoice/quotation templates without a developer or rebuild — required for your "custom option to modify the template" ask. (QuestPDF was considered but is code-first with no end-user visual designer, so it doesn't fit this requirement; it can still be used later for background/system-generated PDFs like GSTR summaries.) |
| Excel import/export | **ClosedXML** (MIT license) | Free for commercial use. EPPlus was ruled out — it requires a paid commercial license for any business use since v5. |
| Tally interoperability | Tally's own XML "Data Interchange" format (file-based) as the primary path; optional live sync later via Tally's XML-over-HTTP "Tally Connector" (default port 9000) | This is the same mechanism Marg itself uses to export into Tally, and what third-party connectors (udiMagic, Biz Analyst, TrulyInvoice) use. Do not build critical functionality on TallyODBC — Tally has moved away from it in favor of XML/HTTP since TallyPrime Release 4.0. |
| Architecture pattern | Repository / unit-of-work abstraction over all DB access, from day one | So the Phase 9 SQL Server migration doesn't require a rewrite |

---

## 4. Feature Modules (functional spec)

### 4.1 Accounting Core
- Chart of accounts: predefined + custom groups/ledgers
- All standard voucher types: Payment, Receipt, Contra, Journal, Sales, Purchase, Credit Note, Debit Note
- Cost centers/categories; multi-company-ready schema even if only one company exists at launch
- Bank reconciliation (manual first; statement import later)
- Immutable double-entry journal: every voucher posts a balanced set of debit=credit journal lines; ledger balances are always **derived**, never stored independently
- **Non-negotiable: Edit Log / Audit Trail.** Every create/alter/delete of a voucher or master is logged with user, timestamp, and before/after values; view-only; cannot be disabled by any user, including admin. (This mirrors Tally's own Edit Log and India's MCA Rule 3(1) requirement for accounting software, in force since 1 April 2023.)

### 4.2 Inventory / Stock Management — single, unified stock ledger
> **Assumption flagged:** "one stock management" is interpreted as one centralized, real-time stock ledger that every module (quotation, order, invoice, purchase, transfer) reads and writes through — never a parallel/duplicate stock-tracking system. Flag this if you meant something different.

- Stock groups/categories, item master (motor/pump fields per Section 2), godowns/warehouses
- Batch tracking + **serial number tracking** (for warranty-traceable units)
- Valuation method: FIFO / weighted average, configurable per item or globally
- Reorder levels & low-stock alerts; stock aging analysis
- Simple bill-of-materials/assembly (e.g., pump + motor sold as a set) — basic only in v1

### 4.3 Sales Pipeline — Quotation with customizable templates
Mirrors Tally's own pattern (a "Quote," an optional voucher under Sales Order that doesn't hit the books until converted):

1. **Quotation/Estimate** — no effect on stock or ledgers; freely re-editable; its own numbering series
2. **Sales Order** — created from a quotation in one click, or from scratch; tracks pending qty/value; "Sales Order Outstanding" report
3. **Delivery/Challan** (optional stage) — for staged deliveries against an order
4. **Tax Invoice** — created from a sales order or directly; posts to accounts + inventory atomically; GST-compliant format (Section 4.5)

Every stage prints from an **editable FastReport template**: logo, layout, visible fields, and terms & conditions must be user-editable, and more than one template per document type should be supported (e.g., a different quotation template per branch or customer category).

Purchase side mirrors this: Purchase Order → Goods Receipt → Purchase Invoice.

### 4.4 Customer Master & Category-Based Pricing
> **Assumption flagged:** placeholder categories are Retailer / Dealer / Wholesaler / Institutional — replace with your actual categories before Phase 4.

- Customer master: contact/GST/billing details, credit limit, credit period
- **Customer Category** master — each customer belongs to exactly one category
- **Category-wise Price List / Margin table** — support both (a) a fixed selling price per item per category, and (b) a margin/discount % over cost or MRP per item per category, since different item lines may need different approaches (Marg supports margin on either a cost or sale basis)
- On quotation/order/invoice entry, the customer's category auto-applies the correct price/margin; a permission-gated manual override is allowed, and the override is logged via the audit trail
- **Customer-wise billing:** per-customer invoice history, running ledger/statement, outstanding receivables, exportable/printable statement

### 4.5 GST & Statutory Compliance
- GST-compliant tax invoice format (Rule 46), HSN/SAC management, reverse charge, composition scheme handling
- GSTR-1 (including the Table 12 B2B/B2C HSN split), GSTR-3B, GSTR-9, GSTR-2A/2B reconciliation, JSON export in the current GSTN schema
- **E-invoicing:** NIC IRP API integration — RSA + AES-256/SEK authentication, IRN generation (SHA-256 hash of Seller GSTIN + Doc No + Doc Type + FY), signed QR code (10 mandatory fields) printed on the invoice, cancel-within-24-hours workflow. Build and test against the **sandbox** (einv-apisandbox.nic.in) first; production only after sandbox sign-off.
- **E-way bill:** Part-A/Part-B workflow, generate directly from an invoice or by IRN, ₹50,000 threshold, validity of 1 day per 200km, cancel-within-24-hours, update vehicle/transporter, extend validity.
- **TDS/TCS:** model sections/rates/thresholds as **data-driven config, not hardcoded**, so the 1 April 2026 Income Tax Act 2025 renumbering (old 194-series sections → new Sections 392/393/394 with numeric payment codes) is a data update, not a rewrite.

### 4.6 Reports / MIS
- Day book, trial balance, balance sheet, P&L, cash/funds flow
- Stock summary, stock aging, reorder report, item-wise sales/margin analysis (useful given category-wise margins)
- Outstanding receivables/payables (bill-wise), customer statement

### 4.7 Data Interoperability — Tally & Excel
- **Export to Tally:** generate Tally-compatible XML ("Data Interchange" format) for masters (ledgers, stock items, groups, units, godowns) and transactions (vouchers), importable via Tally's own Gateway of Tally → Import Data screen. Set a stable GUID per voucher so re-imports don't duplicate — this mirrors Tally's own "overwrite if GUID exists" behavior.
- **Import from Tally:** read XML (and optionally JSON — TallyPrime has supported JSON import since Release 7.0) files exported from Tally/Marg. Do not build critical functionality on TallyODBC (see Section 3); if live, non-file-based sync is wanted later, use the same XML-over-HTTP "Tally Connector" endpoint (default port 9000) that Tally itself and third-party tools use.
- **Excel import/export:** via ClosedXML. For import, follow Tally's own UX pattern — a **column-mapping step** so users can bring their own Excel layout instead of requiring an exact fixed template.
- This module is what lets you migrate existing Tally/Marg data in, and hand data back out to an accountant who works in Tally.

### 4.8 Multi-user / Security (Phase 9+)
- Migrate to SQL Server Express; user roles/permissions; concurrent access. The pricing-override and audit-trail features above become genuinely load-bearing once multiple people can bill at once.

---

## 5. Deliberately Deferred
- Multi-user/LAN networking → Phase 9, not Phase 1. This proves the single-user domain model first and matches how Tally itself separates single-user and multi-user editions. Pull this forward only if concurrent multi-counter billing is a hard day-one launch requirement.
- Payroll beyond statutory basics, manufacturing/BOM depth, cloud/remote access, multi-currency.

---

## 6. Claude Code Project Structure (what Phase 0 must produce)
- **`CLAUDE.md`** at repo root, kept under ~200 lines: tech stack, build/test commands, coding conventions, and a pointer to `/specs`. Situational detail belongs in specs and skills, not here — a bloated CLAUDE.md causes instructions to get silently dropped.
- **`/specs/phase-0-foundations.md` … `/specs/phase-10-hardening.md`** — one file per phase, written before that phase's implementation begins, ending with its checkpoint criteria copied from Section 10. No implementation starts on a phase until its spec file is marked `Status: Approved`.
- **`.claude/agents/*.md`** — one file per subagent (Section 7), each with YAML frontmatter (`name`, `description`, `tools`, `model`) and a scoped system prompt.
- **`.claude/settings.json`** — hooks (Section 8).

---

## 7. Subagents

| # | Agent | Responsibility | Model tier |
|---|---|---|---|
| 1 | `architect-agent` | Cross-module design, ADRs, DB schema approval; keeps `/specs` internally consistent; reviews every other agent's schema/contract changes | Frontier |
| 2 | `database-agent` | EF Core entities/migrations, repository + unit-of-work implementation, Dapper report queries | Frontier |
| 3 | `accounting-agent` | Double-entry journal engine, voucher posting logic, ledger/TB/P&L/BS derivation, edit-log/audit-trail | Frontier |
| 4 | `inventory-sales-agent` | Stock ledger, quotation→order→delivery→invoice pipeline, customer-category pricing/margin engine | Frontier |
| 5 | `compliance-agent` | GST computation, HSN/SAC, GSTR generation, NIC e-invoice/e-way bill API client, TDS/TCS config engine — **highest-risk module, do not downgrade the model tier** | Frontier |
| 6 | `interop-agent` | Tally XML/JSON import-export, Excel import-export via ClosedXML, column-mapping UI | Standard |
| 7 | `ui-agent` | WPF views/viewmodels (MVVM), navigation, FastReport template integration | Standard |
| 8 | `qa-agent` | Writes/runs unit + integration tests, checks each phase's deliverables against its spec's checkpoint criteria, and reports **evidence** (test output, sandbox API responses, screenshots) rather than an assertion of "done" — invoked at the end of every phase | Standard |

---

## 8. Hooks (`.claude/settings.json`)
- **`SessionStart`** — inject the path to the current phase's spec file and its checkpoint checklist into context.
- **`PreToolUse`** (on Write/Edit) — block edits to `/src` unless the relevant `/specs/phase-N-*.md` exists and is `Status: Approved`. This is the spec gate.
- **`Stop`** — block the session from ending until the solution builds, the current phase's test suite passes, and `qa-agent` has produced its evidence report. (Claude Code will override a hook after 8 consecutive blocks — if that happens, stop and get human review rather than letting it force through.)
- **`PreCompact`** — before any context compaction, write the current file list + outstanding TODOs + test commands to `/specs/session-log.md` so nothing is lost across long sessions.

---

## 9. Batch Execution Model
- One phase = one batch = one (or a few) Claude Code sessions.
- **Before** each batch: confirm the phase's spec file is approved.
- **During** each batch: work only within that phase's scope. If scope creep is discovered (e.g., Phase 3 work needs a Phase 4 table), stop and flag it rather than silently expanding scope.
- **After** each batch: `qa-agent` runs the Section 10 checkpoint checks and produces an evidence report; the project owner reviews before the next batch starts.

---

## 10. Phases & Checkpoints

**Phase 0 — Foundations**
- Deliverables: solution scaffold, DI, logging, SQLite + EF Core wired up, repository/UoW abstraction, CI build+test harness, and the actual `CLAUDE.md` / `/specs` / `.claude/agents` / `.claude/settings.json` scaffolding described in Sections 6–8.
- Checkpoint: clean build; empty-schema migration runs; a trivial round-trip test (create + read one entity) passes; all 8 subagent files and both hook types exist and fire correctly on a test edit.

**Phase 1 — Accounting Core**
- Deliverables: masters (groups/ledgers), all core voucher types, journal engine, day book/TB/BS/P&L, edit-log/audit-trail.
- Checkpoint: posting a balanced voucher updates TB/BS/P&L correctly; an unbalanced voucher is rejected; every create/alter/delete of a voucher or master appears in the audit trail with user + timestamp + before/after and cannot be turned off.

**Phase 2 — Inventory & Stock Management**
- Deliverables: stock groups, item master with motor/pump fields, godowns, batch+serial tracking, valuation method, reorder alerts, stock aging.
- Checkpoint: stock valuation reconciles to the inventory GL control account; selling a serial-tracked item correctly records which unit serial was sold.

**Phase 3 — Sales & Purchase Pipeline + Template Designer**
- Deliverables: Quotation → Sales Order → Delivery → Tax Invoice chain (and its Purchase mirror), FastReport integration with at least two editable templates (quotation, tax invoice), atomic posting to accounts + inventory.
- Checkpoint: a quotation converts to an order to an invoice without re-keying data; a non-developer can open the template designer and change the logo or a field without a rebuild; invoice posting is atomic (a failure midway leaves no partial stock/ledger update).

**Phase 4 — Customer & Pricing Engine**
- Deliverables: customer master, customer categories, category-wise price list/margin table, auto-apply-with-override, customer statement/outstanding.
- Checkpoint: selecting a customer on a new invoice auto-fills the correct category price; an override is possible but logged; a customer statement matches the sum of their invoices minus receipts.

**Phase 5 — GST Returns & Tax Invoice Compliance**
- Deliverables: GST computation (CGST/SGST/IGST/cess), HSN/SAC masters, GSTR-1/3B/9 + JSON export, GSTR-2A/2B reconciliation.
- Checkpoint: a generated GSTR-1 JSON validates against the current GSTN schema for a test dataset.

**Phase 6 — E-Invoicing & E-Way Bill**
- Deliverables: NIC sandbox API client (auth, IRN generation/cancellation, signed QR), e-way bill API client (Part-A/B, generate by IRN, cancel/extend).
- Checkpoint: a sandbox-generated IRN and QR print correctly on the tax invoice; an e-way bill is generated by IRN in sandbox; cancellation works within the 24-hour window.

**Phase 7 — TDS/TCS, Payroll Basics, Banking**
- Deliverables: configurable TDS/TCS section/rate/threshold masters, basic payroll statutory output, cheque printing, e-payment/UPI reference fields.
- Checkpoint: a TDS-applicable purchase voucher correctly computes and posts the deduction using the config table, not a hardcoded rate.

**Phase 8 — Data Interoperability**
- Deliverables: Export-to-Tally XML (masters + transactions), Import-from-Tally (XML/JSON), Excel import (ClosedXML + column mapping) and export.
- Checkpoint: a company's masters + transactions exported from this app import cleanly into a real (or sandboxed) TallyPrime company without errors; round-tripping export → import of the same data does not duplicate records.

**Phase 9 — Multi-user / LAN**
- Deliverables: migrate to SQL Server Express, connection/concurrency handling, user roles/permissions.
- Checkpoint: two clients transact concurrently without data corruption or lost updates; permission rules are enforced server-side, not just hidden in the UI.

**Phase 10 — Hardening & Packaging**
- Deliverables: backup/restore, data-at-rest protection, performance/indexing pass, Windows installer.
- Checkpoint: a full backup/restore cycle round-trips without data loss; the installer runs clean on a fresh Windows machine.

---

## 11. Open Assumptions to Confirm Before Phase 1
1. "One stock management" is read as a single centralized, real-time stock ledger shared by every module — confirm.
2. Exact customer categories and whether margin is figured on cost, on sale price, or needs to support both.
3. Multi-user is deferred to Phase 9 — confirm this doesn't conflict with a day-one launch need for concurrent billing.
4. The motor/pump item attributes in Section 2 — confirm or extend the list.