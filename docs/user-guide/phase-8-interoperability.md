# Import and export — running it from the screen

Three screens under **Import & Export**: Export, Import, and Column mappings.

---

## Sending the month to your accountant

**Import & Export → Export**

1. Tick **Vouchers**. Tick **Masters** too the first time, or whenever you have added ledgers or
   items since the last file — the accountant's Tally needs those before it can accept the vouchers
   that name them.
2. Leave **Format** on **Tally XML**. That is the format every version of TallyPrime reads.
3. Set **From** and **To** to the month. Tick **Whole book** only when setting up a fresh Tally
   company.
4. Press **Preview**. Check the counts look like the month you had, and that the debit and credit in
   the totals band are equal.
5. Press `Ctrl+E` and save the file.

Send that file. Your accountant opens it in Tally through **Gateway of Tally → Import Data**.

**Sending the same month twice is safe.** Every record carries an identifier, and Tally updates a
record it already holds rather than adding a second one. Correct a voucher here, re-send the month,
and the accountant's books correct themselves.

### If the export is refused

> *These vouchers do not balance among themselves.*

Something is wrong with the selection rather than with the file. Widen the dates — a voucher whose
lines somehow fall either side of the period boundary is the usual cause — and if it persists, check
the Trial Balance for the same dates.

---

## Bringing your old Tally data in

This is the migration. Do it once, carefully, before you start billing from this application.

**Import & Export → Import**

1. `Alt+F`, and pick the file your accountant exported from Tally.
2. Tick **Masters** only on the first run. Get the chart of accounts and the item list right, look at
   them on their own screens, and only then come back for the vouchers.
3. Press **Check**. Nothing is written yet.
4. Work through the problem list. Each line says **where** in the file, **which** record, and **what**
   is wrong with it. Fix them in the source file.
5. Press **Check** again. Repeat until the list is empty.
6. Press `Ctrl+A`.

**One problem stops the whole file.** Nothing is written until the list is empty — there is no such
thing as a half-finished import here, because a half-finished migration is the state there is no way
back from.

### Reading the counts

The band above the problem list says, for each kind of record, how many are **new** and how many are
**existing**. On a first migration everything should be new. On a re-run everything should be
existing — if something still says new on the second pass, the file has changed since the first.

### Reading the reconciliation

After it commits, the band at the bottom says whether the books moved by exactly what the file
carried. When they did, it says so and names the figures. When they did not, it names the difference
— and that is worth stopping for, because it means something in the file did not land the way it read.

### Warnings

Warnings appear in their own grid and do **not** stop the import. The one you will see is about
serial-tracked items: a Tally file carries the quantity but not the serial numbers, so the stock comes
in without them. Record those serials before issuing any of that stock.

---

## Importing a spreadsheet

Any `.xlsx` — a supplier's price list, a stock count someone typed up, an old ledger listing.

### First, teach it the layout

**Import & Export → Column mappings**

1. Set **Brings in** to what the sheet holds — **Items** for a price list, **Ledgers** for a party
   list.
2. Press `Alt+F` and pick the file. It reads the headers and fills the grid with its best guess.
3. Work down the **Fills this field** column. Leave blank anything this application has no use for —
   a Warranty column on a price list is not a problem, it is just not ours.
4. If a red line above the grid says something is still unmapped, fill that in; the sheet cannot be
   read without it.
5. Give the mapping a name you will recognise next year and press `Ctrl+A`.

**You do not have to change the supplier's sheet.** The columns can be in any order, the headers can
be worded however they are worded, and there can be columns we ignore entirely. That is what the
mapping is for.

### Then import through it

Back on **Import**, choose the `.xlsx`, pick the mapping from the picker that appears, and carry on as
above — Check, fix, `Ctrl+A`.

### Vouchers in a spreadsheet

One row per line, and **the rows of one voucher must be together**. A two-line journal looks like:

| Vch No | Vch Date | Type | Ledger | Dr | Cr |
|---|---|---|---|---|---|
| JRN/0001 | 2026-04-15 | Journal | Cash | 1500.00 | |
| JRN/0001 | 2026-04-15 | Journal | Sales | | 1500.00 |

Repeating the date and type on the second row is fine; leaving them blank is also fine.

---

## Getting a spreadsheet out

On **Export**, set **Format** to **Excel**. One sheet per record kind, with our own field names as the
headers — so you can edit it and import it straight back, and the mapping for it needs no thought.

It is also the quickest way to answer a supplier who asks what layout to send: export an empty one and
forward it.

---

## Keys on these screens

| Key | Export | Import | Column mappings |
|---|---|---|---|
| `Alt+F` | — | Choose file | Read a sheet |
| `Ctrl+E` | Write file | — | — |
| `Alt+C` | — | — | New mapping |
| `Ctrl+A` | Write file | Import | Save |
| `Alt+D` | — | — | Deactivate |
| `Alt+X` | Clear | Clear | Reload the selected mapping |

---

## What is not proven yet

Nobody has yet imported one of these files into a real TallyPrime company, because no export of your
actual books has been supplied to test against (`specs/open-questions.md` #22). Everything is written
to the published format and round-trips correctly through this application, but the first real
exchange with your accountant should be treated as a test: send one month, have them import it, and
check a few figures against what this application shows before relying on it.
