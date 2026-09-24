# Tally interchange — export and import

The books written out as a file the accountant's Tally can read, and a file from Tally read back into
the books. Both directions go through one vendor-neutral model, so a field mapped once is mapped both
ways (`specs/phase-8-interoperability.md` §4a D3).

## User workflow

### Handing data to the accountant

1. Open **Import & Export → Export**.
2. Tick **Masters**, **Vouchers**, or both, pick a **Format**, and set the period — or tick
   **Whole book**.
3. Press **Preview**. The list shows how many of each record kind the export holds, and the totals
   band shows the vouchers' debit and credit.
4. `Ctrl+E` writes the file.

An export whose vouchers do not balance is refused. Every voucher in the books balances by
construction, so an unbalanced export means the selection dropped something.

### Bringing Tally data in

1. Open **Import & Export → Import** and press `Alt+F` to choose the file.
2. Press **Check**. Nothing is written. The grid lists every problem in the file — where it is, which
   record, and what is wrong with it — and the band above counts what would be created and what is
   already in the books.
3. Fix the source, press **Check** again. Repeat until the list is empty.
4. `Ctrl+A` imports. One problem anywhere stops the whole file; there is no partial import.

After it commits, the **Reconciliation** band says whether the books moved by exactly what the file
carried. If they did not, it names the difference.

Running the same file twice is safe. The second run reports every record as already in the books and
changes nothing — a voucher whose content has not changed does not even get a new revision.

## Developer notes

| Piece | Where | What it is |
|---|---|---|
| `InteropDocument` | `Application/Interop/InteropContracts.cs` | The vendor-neutral model both directions read. Amounts and quantities in minor units (ADR-0003) |
| `TallyConventions` | `Application/Interop/` | Every encoding rule the Tally format imposes, in one place — dates, the amount sign, quantity-with-unit, voucher type names |
| `TallyXmlWriter` / `TallyXmlReader` | `Application/Interop/` | The XML envelope, sharing element names through `TallyXmlNames` |
| `TallyJsonWriter` / `TallyJsonSource` | `Application/Interop/` | The same envelope as JSON, transcoded rather than re-implemented |
| `TallyXmlSource` | `Application/Interop/` | Streams the file one `TALLYMESSAGE` at a time |
| `InteropExportService` | `Application/Interop/` | The books read into the model |
| `InteropImportService` | `Application/Interop/` | Two passes: validate everything, then write it inside one transaction |
| `ValidationPass` | `Application/Interop/` | Pass one. Every problem in the file, in one read |
| `InteropMasterIndex` | `Application/Interop/` | What the company already holds, so a record can be told new from seen-before |
| `InteropReconciliation` | `Application/Interop/` | C8.6 — what the books moved by, against what the file carried |

### The sign convention

Tally writes a **debit as a negative** `AMOUNT`, the opposite of `JournalLine.AmountMinor`. This is the
rule most worth knowing about and the one that is invisible when wrong: invert it on both sides and
every round-trip test still passes while every voucher handed to the accountant is reversed. It lives
in `TallyConventions.Amount` and is asserted against the rendered file, not through a round trip.

### Identity, and why a re-import does not duplicate

Every record carries the stable entity GUID assigned at construction (ADR-0001). Tally overwrites a
record whose GUID it already holds, which is the whole mechanism behind C8.2. On the way back in, the
importer matches by GUID first and by normalised name second — the GUID for anything this application
exported, the name for anything created in Tally, which has never seen our identifiers.

Vouchers match by voucher type, number and financial year, which is exactly what the numbering index
enforces. A voucher already in the books and unchanged is left alone; one that differs is altered,
appending a revision (ADR-0002) with the file named as the change reason.

### What is not exported, and why

**System account groups.** Sundry Debtors, Cash-in-Hand, Sales Accounts and the rest are Tally's own
standard tree and exist in every Tally company under those names. Ledgers still name them as parents
and Tally resolves a parent by name.

**GUIDs on system masters.** Tally's built-in Cash and Profit & Loss A/c already have identifiers of
Tally's own. Sending ours against the same name asks the importer to reconcile two identities for one
record; omitting it lets the name match.

**Voucher types are exported** even though §2's list did not name them — see §4b B1 of the spec. A
company's voucher types are its own ("Stock Journal", "Cash Sales"), and a file that referenced one
without defining it leaves the reader unable to tell which posting behaviour it carries.

### Everything unverified is in one file

No real Tally export of the owner's books has been supplied (`specs/open-questions.md` #22). Every
rule in `TallyConventions` — and the assumption in `ReportNameFor` that Tally reads messages in
document order — is written from the published format and is unverified on real data. They are
collected in that one type precisely so each is a one-line fix when a real file arrives.

## Verification

`tests/VinayakaAgencies.Application.Tests/Interop` — round trips through both formats from one test
body, full-scale precision, the sign convention on the rendered text, stable output across writes,
and the readers' behaviour on malformed and unrecognised input.

`tests/VinayakaAgencies.Infrastructure.Tests/Interop/InteropWorkflowTests.cs` — the database claims:
a re-import that adds no row and no revision (C8.2), a migration into a second database reproducing
the source's trial balance and closing stock (C8.6), one unbalanced voucher committing nothing and
naming its line (C8.4), four decimal places surviving the whole path (C8.5).

**Not yet done:** C8.1 — importing into a real TallyPrime company — and C8.6 against real data. Both
wait on the file tracked as open question #22.
