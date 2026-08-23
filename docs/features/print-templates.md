# Feature — Print Templates, Designer & Preview

**Status: implemented, Phase 3** (spec `specs/phase-3-sales-purchase-pipeline.md` §2; ADR-0004).

## What & why

Every trade document prints from a **template stored as data** — title, logo switch and path,
header lines (address/GSTIN/bank), visible item-grid columns, footer note, terms & conditions and
the GST Rule 46 copy labels. An owner edits any of it in the built-in designer screen: no rebuild,
no developer, no FastReport licence needed yet (checkpoint C3.2; ADR-0004 records why rendering is
the built-in WPF `FlowDocument` engine today — swapping to FastReport later is presentation-layer
work because templates are data either way).

## User workflow

Sales & Purchase → **Print templates**:

1. Pick a kind filter, **Load selected**, edit the fields on the right (tick columns on/off, type
   header lines one per line, `;`-separate copy labels), **Save as new version**. Saving appends
   version *n+1*; earlier versions stay listed for the audit and nothing ever edits history.
2. **Activate / retire** flips whether a template is a default candidate.
3. In **Documents**, print a document: the default template resolves automatically — among active
   templates of the kind, lowest priority wins (C3.6) — and preview/print offers every Rule 46
   copy label as its own page.

Two tax-invoice templates are seeded at provisioning ("Detailed", priority 10; "Compact",
priority 20), which is C3.6's demonstration set as well as the working default.

## Developer notes

- `PrintTemplate` (identity, priority, active flag, current-version pointer) + append-only
  `PrintTemplateRevision` rows carrying the layout JSON (`PrintTemplateService.Serialise`, one
  serialiser so writer and reader cannot drift). Versions advance exactly one at a time
  (`AttachRevision` refuses skips).
- `PrintTemplateService`: list/resolve-default/get/create/save-layout/set-active. Retiring the
  last active template of a kind is refused — a kind must always have something to print with.
- Rendering lives in `VinayakaAgencies.App/Presentation/TradeDocumentPrinter.cs`: pure function of
  `(summary, layout, companyName, copyLabel)` → `FlowDocument`; shown by `PrintPreviewWindow`
  with per-copy-label pages. No data access in the printer.
- Seeding runs through `ICompanyProvisioningService.EnsurePipelineSeedAsync` (runtime, audited,
  idempotent) — never `HasData`, like every other seed table (spec §5).

## Verification

`tests/VinayakaAgencies.Infrastructure.Tests/Printing/PrintTemplateWorkflowTests.cs`:

- C3.6 `PipelineSeed_CreatesTwoTaxInvoiceTemplates_LowestActivePriorityIsDefault`,
  `SetActive_RetiringTheDefault_FallsBackToNextPriority`,
  `SetActive_RetiringTheLastActiveTemplateOfAKind_IsRefused`
- C3.2 `SaveLayout_AppendsANewVersionAndHistoryStaysReadable`;
  `LayoutJson_RoundTripsThroughTheOneSerialiser`.
