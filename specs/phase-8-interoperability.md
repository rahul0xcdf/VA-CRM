# Phase 8 — Data Interoperability (Tally & Excel)

```yaml
Phase: 8
Title: Data Interoperability
Status: Draft
Source: project.md §4.7, §10 (Phase 8)
Depends on: Phases 1–5
Agent: interop-agent
```

> **Draft.** Do not implement until `Status: Approved`.

## 1. Deliverables (project.md §10)

Export-to-Tally XML (masters + transactions), Import-from-Tally (XML/JSON),
Excel import (ClosedXML + column mapping) and export.

## 2. Scope notes

- **Export to Tally:** Tally's own XML "Data Interchange" format, for masters (ledgers, stock items,
  groups, units, godowns) and transactions (vouchers), importable via Gateway of Tally → Import Data.
  A **stable GUID per voucher and per master** (assigned at creation — see Phase 0 `Entity`) so a
  re-import overwrites rather than duplicates, matching Tally's own "overwrite if GUID exists" behaviour.
- **Import from Tally:** read XML, and optionally JSON (supported since TallyPrime Release 7.0),
  exported from Tally or Marg. This is the migration path for existing data.
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

## 4. Open questions to close before approval

- Which TallyPrime release is the accountant on? (Determines XML/JSON support.)
- Is there existing Tally or Marg data to migrate in, and roughly how much?
- Which direction matters more day to day — hand data to the accountant, or pull data in?
