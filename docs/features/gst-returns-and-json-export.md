# GST returns & JSON export

**Phase 5 · GST Returns & Tax Invoice Compliance** (`specs/phase-5-gst-returns.md` §2)

## Feature

Period returns built from posted documents: **GSTR-1** (B2B invoices, B2C others, Table 12 HSN
summary split B2B/B2C, document-issue summary), **GSTR-3B** (outward supplies, ITC, net tax —
cross-checked against GSTR-1 and the books, C5.3), annual **GSTR-9**, and **GSTR-1 JSON export**
in the pinned GSTN offline-utility schema (**v3.2.4**, stamped with version + generation
timestamp on every export). Export/SEZ/e-commerce tables are omitted entirely per spec §4a.

## User workflow (GST → GST returns)

1. Pick the month and return type; the summary renders in sections like the portal's.
2. For GSTR-1, press **Export JSON** (Ctrl+A) and choose a save location — upload that file via
   the portal/offline utility. The status line shows the schema version used.

## Developer notes

- `IGstReturnService` (`Application/Gst/GstReturnService.cs`) reads via Dapper reporting queries
  (`GstQueryKeys` + `Infrastructure/Data/GstSqliteStatements.cs`); totals derive from posted
  invoice snapshots, so 3B == GSTR-1 == books by construction (C5.3).
- `Gstr1JsonExporter` writes System.Text.Json; version constants in `GstSchemaVersions`.
- Reconciliation of imported **GSTR-2B** files against purchase records classifies lines
  Matched / MissingInBooks / MissingInPortal (C5.4) — see `docs/features/` sibling doc
  `gstr-2b-reconciliation.md` workflow on the *GST → GSTR-2B reconciliation* screen.

## Verification

- C5.1 structural self-consistency + stamp assertions, C5.3 reconciliation equality,
  C5.4 classification fixtures — `tests/VinayakaAgencies.Infrastructure.Tests/Gst/` and
  Application Gst tests. Full suite green at implementation.
