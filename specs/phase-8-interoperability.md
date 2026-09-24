# Phase 8 — Data Interoperability (Tally & Excel)

```yaml
Phase: 8
Title: Data Interoperability
Status: Approved
Source: project.md §4.7, §10 (Phase 8)
Depends on: Phases 1–5 (export also carries Phase 6–7 vouchers — they post through the same journal path)
Agent: interop-agent
```

> **Approved 24 Sep 2026** by the project owner, with the §4 questions closed into §4a below.

## 1. Deliverables (project.md §10)

Export-to-Tally XML (masters + transactions), Import-from-Tally (XML/JSON),
Excel import (ClosedXML + column mapping) and export.

## 2. Scope notes

- **Export to Tally:** Tally's own XML "Data Interchange" format, for masters (ledgers, stock items,
  groups, units, godowns) and transactions (vouchers), importable via Gateway of Tally → Import Data.
  A **stable GUID per voucher and per master** (assigned at creation — see Phase 0 `Entity`) so a
  re-import overwrites rather than duplicates, matching Tally's own "overwrite if GUID exists" behaviour.
- **Import from Tally:** read XML and JSON (JSON supported since TallyPrime Release 7.0; both in scope per §4a D1),
  exported from Tally. This is the migration path for existing data. Marg is out of scope (§4a D5).
- **Do not build on TallyODBC** (`project.md` §3) — Tally has moved to XML/HTTP since TallyPrime 4.0.
  If live sync is wanted later, use the same XML-over-HTTP "Tally Connector" endpoint (default port
  9000) that Tally and third-party tools use. File-based first; HTTP is a later, optional addition.
- **Excel via ClosedXML** (MIT). **Never EPPlus** — paid licence for business use since v5.
- **Import UX follows Tally's own pattern: a column-mapping step.** Users bring their own sheet
  layout; they are not forced into a fixed template. Mappings are saveable and reusable.
- Every import is a dry-run-then-commit: validate the whole file, show a per-row error report, and
  only then write, inside one transaction.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C8.1 | A company's masters + transactions exported from this app import cleanly into a real (or sandboxed) TallyPrime company without errors |
| C8.2 | Round-tripping export → import of the same data does not duplicate records |

Additional:

| # | Criterion |
|---|---|
| C8.3 | An Excel item import with a non-standard column order succeeds via the mapping step |
| C8.4 | An import with one invalid row commits nothing and reports the offending row and reason |
| C8.5 | Amounts and quantities survive a round-trip with no precision loss |

## 4. Open questions — closed 24 Sep 2026

- ~~Which TallyPrime release is the accountant on?~~ → D1
- ~~Is there existing Tally or Marg data to migrate in, and roughly how much?~~ → D2
- ~~Which direction matters more day to day?~~ → D3

## 4a. Decisions (24 Sep 2026)

### D1 — TallyPrime 7.0 or newer: build both XML and JSON

The accountant is on **TallyPrime 7.0+**, so JSON is available on both sides and is in scope
alongside XML.

XML stays the primary and mandatory path — it is what C8.1 is proved against, because it is the
format every release since 4.0 reads, and therefore the one that still works if the accountant's
install is rolled back or a second accountant turns out to be on an older release. JSON is built as a
**second serialiser over the same canonical document model**, not a second pipeline: one mapping
layer, two writers, two readers. That is the only shape in which a second format is cheap, and C8.2
enforces it — the round-trip runs through **both** formats.

### D2 — A full history is being migrated in: import is a first-class half of the phase

This is the answer that costs the most. It turns the phase from "write an exporter, and an importer to
match" into a **migration with an exporter attached**. Three consequences, all load-bearing:

**A real export file is required, and is now a tracked dependency** (`open-questions.md` #22). A Tally
export of the actual books is the only thing that proves the importer. Field names, date formats, the
exact shape of a multi-ledger voucher, which optional elements the accountant's configuration actually
emits — none of that is knowable from the published format alone, and a fixture written from the spec
would prove only that we can read our own assumptions back. Phase 6 already shows where that ends:
`IIrpClient` is correct against a recorded fake and still unverified on the wire. **Implementation
proceeds without it** — the importer is built against fixtures derived from this app's own exports —
but C8.1 and the migration itself are not signed off until a real file has been through.

**Volume is a design constraint, not a later tuning pass.** "Validate everything, then write in one
transaction" (§2) is correct and is kept, but a full history is not a hundred rows. The importer
streams the source file rather than materialising it, batches its inserts, and reports progress; the
all-or-nothing transaction stays, because a migration that half-succeeds is worse than one that
fails outright. The per-row error report (C8.4) is what makes a large file tractable — the realistic
migration is: run, read the report, fix the source, run again, several times over.

**The opening position must reconcile.** A migrated history is only correct if the trial balance and
the stock valuation after import equal those in Tally before it. That becomes C8.6 below rather than
something to notice afterwards, because it is the one failure that looks like success: every row
imports, no error is reported, and the books are wrong.

### D3 — Both directions matter equally: one canonical model, both built together

No sequencing preference, so the two directions are built as **one mapping layer read in both
directions**, not as an exporter and an importer that happen to agree. A field mapped once is mapped
for both, which is what makes C8.2 a real test rather than a coincidence: a round-trip through a
shared model cannot silently drop a field only one side knew about.

Excel follows the same rule — the column-mapping step (§2, C8.3) maps the user's sheet onto that same
canonical model, so Excel import and Tally import converge before anything reaches the database and
there is exactly one validation-and-commit path to test.

### D4 — Cost-centre naming (open question #18) is now live

§2's import matches cost centres **by name**, which makes #18 — unique per company, or per category? —
a Phase 8 question rather than a deferred one. The working assumption stays company-wide. Nothing
blocks implementation, but it is closed before the first real import runs: a migration is precisely
when duplicate names arrive from outside and get silently merged.

### D5 — Marg import deferred; the importer targets Tally only

The accountant works in **Tally**, so the importer is built and verified against Tally's XML and JSON
and nothing else. Marg is out of Phase 8 scope, and §2's "exported from Tally or Marg" is narrowed
accordingly.

This is a scope cut, not a design change, and that distinction is the reason it is safe. The reader
targets the canonical model of D3, not a vendor: whatever Marg would need later is a source-format
adapter in front of a model that already exists, not a second import pipeline. Nothing built in this
phase has to be undone to add it.

## 4b. Changes beyond the spec, and why

### B1 — Voucher types are exported as masters

§2 lists the masters to export as "ledgers, stock items, groups, units, godowns". Voucher types are
added to that list, because without them the transactions half of the same file cannot be imported by
anyone.

A company's voucher types are its own: "Stock Journal", "Cash Sales", "Bank Payment". None of those is
a name Tally ships with, so a file that referenced one without defining it leaves the reader with a
document and no way to know which of the eight posting behaviours it carries. Inferring one from the
name is the tempting answer and the wrong one — guess that "Bank Payment" is a payment and you happen
to be right; guess wrong on some other name and real money posts down the wrong path with nothing in
the file to show it happened. The exported `VOUCHERTYPE` element declares its behaviour through the
built-in type it inherits from, which is how Tally itself expresses the same thing, and it is equally
needed in the other direction: a voucher filed under a type the accountant's company does not hold
would be rejected there too (C8.1).

### B2 — A whole-life journal totals query

`Interop.JournalTotals`, one statement per provider, added for the C8.6 reconciliation §4a D2
introduced. The trial balance could not serve: `GetTrialBalanceAsync` resolves its date to a financial
year, so a migration carrying five years of history would have reconciled against one of them and
reported a difference that was not real. The new query reads whole-life debit and credit through the
same current-revision predicate every other reporting statement uses.

### B3 — Two entities, one migration

`ExcelColumnMapping` and `ExcelColumnMappingColumn` (migration `AddExcelColumnMappings`). §2 already
requires mappings to be "saveable and reusable", so this is the spec's own requirement rather than an
addition — recorded here because it is the only schema Phase 8 adds. Everything else in the phase
reads and writes files.

### B4 — A Phase 2 defect fixed on the way

`StockReportService.ReconcileValuationAsync` threw on a company with no stock movements. Dapper builds
its deserialiser from the reader's schema before it knows whether any rows came back, and on an empty
result SQLite reports no type for an aggregate expression — so the positional record was asked for a
`byte[]` constructor and found none. Every other report in the suite returns a row per master and
never reaches the condition. Found here because the import reconciles against a fresh company; it
would otherwise have surfaced as the Stock Valuation screen throwing on a new install.

## 5. Checkpoint addition from §4a

| # | Criterion |
|---|---|
| C8.6 | After importing a Tally export, the trial balance and closing stock valuation in this app equal those in the source company (D2) |
