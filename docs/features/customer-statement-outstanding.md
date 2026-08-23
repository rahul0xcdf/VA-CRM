# Customer statement & bill-wise outstanding

**Phase 4 · Customer & Pricing Engine** (`specs/phase-4-customer-pricing.md` §2, checkpoints
C4.3/C4.6)

## Feature

Two customer-facing reads over the books — nothing stores a balance:

- **Statement of account**: opening balance, one row per posting against the party's ledger
  (invoices debit, receipts credit), running balance, closing. Printable and exportable.
- **Bill-wise outstanding**: posted invoices still open, with receipts shared out
  **oldest-bill-first**, exactly how Tally applies on-account money (C4.6). Fully paid bills
  drop off; an empty list is a clear account.

## User workflow (Reports → Customer statement)

1. Pick a customer (deactivated ones stay listed — statements outlive activity) and a date
   range; press **Load statement**.
2. The statement grid shows every posting with its running balance; status line summarises
   closing balance vs open bills.
3. The **Open bills** grid shows each unpaid invoice: billed / paid so far / outstanding.
4. **Print / preview** lays the statement + open bills out as plain A4 with the company name;
   **Export CSV** writes the statement rows (invariant format) to a chosen file.

## Developer notes

- Service `ICustomerStatementService` (`Application/Customers/CustomerStatementService.cs`);
  both reads reuse the Phase 1 reporting seam (`LedgerVouchersForPeriod`,
  `LedgerBalancesAsOf`) — no new SQL.
- Statement rows arrive as decimals converted once from minor units in the query mapper
  (ADR-0003); `CustomerStatement`/`OpenBillRow` carry no mutable state.
- Bill-wise allocation is derived at read time: open invoices ordered by date, unallocated
  receipt pool applied oldest first.
- Printing: `Presentation/CustomerStatementPrinter.Build(...)` → WPF `PrintDialog`, following
  the trade-document printer's "works before any template exists" philosophy.

## Verification

- **C4.3**: statement test asserts closing = Σ invoices − Σ receipts for the period.
- **C4.6**: two invoices (older ₹1,000, newer ₹400) + one ₹600 receipt allocate oldest-bill
  first: older opens at ₹400, newer at ₹400; statement closes at ₹800. Stable across repeated
  runs (`tests/VinayakaAgencies.Infrastructure.Tests/Customers/CustomerWorkflowTests.cs`).
