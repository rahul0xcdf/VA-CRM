namespace VinayakaAgencies.Application.Customers;

/// <summary>One posting on a customer's statement, with the running balance after it.</summary>
/// <remarks>
/// Debit is a bill raised to the customer; credit is money received or a note against them.
/// Amounts are decimals converted once from minor units by the ledger-statement query's mapper.
/// </remarks>
public sealed record CustomerStatementRow(
    DateOnly Date,
    string VoucherNumber,
    string VoucherTypeName,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>A customer's account statement: opening, movements, closing (checkpoint C4.3).</summary>
public sealed record CustomerStatement(
    Guid CustomerId,
    string CustomerName,
    Guid LedgerId,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal OpeningBalance,
    IReadOnlyList<CustomerStatementRow> Rows,
    decimal ClosingBalance);

/// <summary>One posted invoice of the bill-wise outstanding report, with its allocation state.</summary>
/// <param name="PaidAmount">Receipts allocated to this bill so far — oldest bills are paid first.</param>
/// <param name="OutstandingAmount">What is still open on this bill.</param>
public sealed record OpenBillRow(
    Guid InvoiceId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    decimal BilledAmount,
    decimal PaidAmount,
    decimal OutstandingAmount);

/// <summary>
/// The customer-facing reads over the books: the printable statement and the bill-wise
/// outstanding report (phase-4-customer-pricing.md §2, checkpoints C4.3 and C4.6).
/// </summary>
/// <remarks>
/// Both read exactly what the journal holds — nothing here stores a balance. The statement walks
/// the party ledger's postings through the Phase 1 reporting seam; bill-wise derives each bill
/// from its posted tax invoice and shares the unallocated receipt pool out oldest-bill-first.
/// </remarks>
public interface ICustomerStatementService
{
    /// <summary>
    /// The statement for one customer between two dates: opening balance, one row per posting
    /// against their ledger, running balance and closing.
    /// </summary>
    Task<CustomerStatement?> CustomerStatementAsync(
        Guid customerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posted invoices still open for one customer, receipts applied oldest-bill-first (C4.6).
    /// Fully paid bills are omitted; an empty list means the account is clear.
    /// </summary>
    Task<IReadOnlyList<OpenBillRow>> BillwiseOutstandingAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
