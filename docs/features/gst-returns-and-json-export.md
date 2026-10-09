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
- **The emitted field names are only as confirmed as C5.1 has managed.** Cess is `csamt`, the HSN
  code is `hsn_sc`, and a B2B invoice's `itms` entries nest their figures under `num` + `itm_det`:
  all three were wrong until the Oct 2026 C5.1 review, and `csft` in particular would have filed
  every cess amount as nil. Still unverified and probably wrong — `open-questions.md` #23 — are the
  B2C section shape (the portal's is a flat `b2cs`, not our nested `b2c`), the absent `b2cl`
  section, the `gtotals` block (portal: `gt` / `cur_gt`) and the `uqc` hardcoded to `NOS`. Do not
  "tidy" any of these from memory; they need the authoritative schema.
- The emitted shape is locked by a JSON Schema evaluator against
  `tests/VinayakaAgencies.Infrastructure.Tests/Gst/Schemas/gstr1-as-built.schema.json`
  (`additionalProperties: false` throughout). **Changing a field in the exporter means changing
  that schema too, deliberately** — which is the point. Drop the real GSTN schema into the same
  folder as `gstr1-gstn.schema.json` to close C5.1; the csproj globs the folder.
- `BuildGstr9Async` steps whole calendar months from the first of the opening month and clamps each
  window to the requested period at both ends. It used to step from `fromDate` itself, which left a
  hole for any period not starting on the 1st and silently dropped invoices in it.
- Reconciliation of imported **GSTR-2B** files against purchase records classifies lines
  Matched / MissingInBooks / MissingInPortal (C5.4) — see `docs/features/` sibling doc
  `gstr-2b-reconciliation.md` workflow on the *GST → GSTR-2B reconciliation* screen.

## Verification

- C5.1 structural self-consistency + stamp assertions, C5.3 reconciliation equality,
  C5.4 classification fixtures — `tests/VinayakaAgencies.Infrastructure.Tests/Gst/` and
  Application Gst tests.
- `ExportGstr1_FromPostedInvoices_ValidatesAgainstTheAsBuiltSchema` validates a real export against
  the schema above; `Gstr1SchemaValidationTests` proves the schema actually bites (an empty object,
  an unknown field and both pre-review shapes all fail) so the validation cannot become theatre.
- GSTR-9: `BuildGstr9_OverAFinancialYear_ReconcilesToItsMonthlyGstr1sAndTheBooks` compares the
  annual total against its own monthly rows, twelve independently built monthly GSTR-1s, **and**
  the posted snapshots. All three, because the window defect made the first two agree while both
  were wrong. `BuildGstr9_ForAPeriodNotStartingOnTheFirstOfAMonth_LosesNoInvoice` is that
  regression.
- See `specs/evidence/phase-5-evidence.md` §5 for the four defects these tests found and §1 for
  what C5.1 still needs.
