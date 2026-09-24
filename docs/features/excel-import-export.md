# Excel import and export

Bring in the sheet you already have, in the column order it already has, and get one back you can
edit and hand straight in again (`specs/phase-8-interoperability.md` §2, checkpoint C8.3).

## User workflow

### Teaching it your sheet's layout

1. Open **Import & Export → Column mappings**.
2. Choose what the sheet **brings in** — items, ledgers, units, stock groups, godowns, account groups
   or vouchers.
3. Press `Alt+F` and pick the workbook. It reads the headers and fills the grid, guessing which field
   each column feeds. Columns it could not place are left blank.
4. Fill in the blanks, leave the ones this application has no use for empty, and name the mapping.
5. `Ctrl+A` saves it. Next month it is in the picker.

If a required field is still unmapped, the screen says which one above the grid, before you save
rather than after.

### Importing through it

On **Import & Export → Import**, choose an `.xlsx` file and a mapping appears to be picked. Everything
after that is the same as a Tally file — Check, read the report, fix the source, `Ctrl+A`.

### Exporting to Excel

On **Import & Export → Export**, choose **Excel** as the format. You get one sheet per record kind,
headed with this application's own field names — so an export maps onto itself, and you can edit it
and import it straight back with a mapping that needs no thought.

### A voucher over several rows

One row per line, grouped by voucher number. **Rows belonging to one voucher must be consecutive.**
The voucher's own fields — date, type, narration — may be repeated on every row or filled only on the
first.

## Developer notes

| Piece | Where | What it is |
|---|---|---|
| `IWorkbookReader` / `IWorkbookWriter` | `Application/Interop/IWorkbook.cs` | The ports. Cells cross as invariant text |
| `ClosedXmlWorkbookReader` / `Writer` | `Infrastructure/Interop/` | The ClosedXML adapters. **Never EPPlus** — paid licence since v5 |
| `ExcelInteropSource` | `Application/Interop/` | The mapped sheet as `InteropRecord`s, identical to what the Tally readers produce |
| `InteropFieldCatalogue` | `Application/Interop/` | Which fields each target accepts, and which are required |
| `ExcelColumnMapping` | `Domain/Interop/` | The saved mapping and its columns |
| `ExcelMappingService` | `Application/Interop/` | List, edit, and the first guess at a new one |
| `InteropWorkbookExporter` | `Application/Interop/` | The document as sheets. `BuildSheets` is pure, so the layout is testable |

### Why the port

The same arrangement as `IIrpClient` in Phase 6, for two reasons. It keeps a file-format library out
of a layer whose job is to declare what it needs; and it puts the mapping and validation rules —
everything worth testing — somewhere a test can reach with an in-memory grid.

What a fake cannot prove is that Excel's own typing survives, so the adapters are tested against real
`.xlsx` files written and read back.

### Cells cross as text, and that is the adapter's doing

Excel stores a date as a serial number and a currency as a formatted double. The adapter normalises: a
date cell becomes `yyyy-MM-dd`, a numeric cell an invariant decimal with no separators. Passing the
raw values up would give "45231" on one machine and "15/04/2026" on another, and nothing above that
line could see the difference.

The writer writes everything as text for the mirror-image reason: a number written as a number is held
as a double, and ₹1,234.5678 written and read back would not necessarily be ₹1,234.5678 (C8.5).

### Matching, and why it is by header

A mapping matches the owner's column **headers**, normalised — so "Item Name", "ITEM NAME" and
"Item  Name" are one column. Matching by position would mean a supplier who inserts a column in the
middle of their price list shifts every field one to the right, silently, and the import succeeds with
every value in the wrong field.

The suggestion heuristic is more generous than the matcher: it compares letters and digits alone, so
"Base Unit" finds `BaseUnit`. Being generous costs a correction; being strict costs the whole grid.

## Verification

`tests/VinayakaAgencies.Application.Tests/Interop/ExcelInteropSourceTests.cs` — C8.3 with the columns
in an order nothing could have anticipated and two unmapped columns in the middle; headers on a row
other than the first; a mapped header the sheet does not have; a required field nobody mapped; a
voucher across two rows; amounts with separators and a currency symbol.

`tests/VinayakaAgencies.Infrastructure.Tests/Interop/ExcelWorkbookTests.cs` — the same against real
`.xlsx` files, plus the suggestion heuristic and an export that maps onto itself.

`tests/VinayakaAgencies.App.Tests/ViewModels/Phase8ScreenTests.cs` — the mapping screen's rules:
what is still unmapped, and what happens to a line when the target changes under it.
