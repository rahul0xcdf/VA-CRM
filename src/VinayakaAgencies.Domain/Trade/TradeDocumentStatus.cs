namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// The lifecycle of a trade document. Which transitions a kind allows is the kind's business,
/// enforced on <see cref="TradeDocument"/>; the enum only names the states.
/// </summary>
public enum TradeDocumentStatus
{
    /// <summary>Not yet effective: quotations before conversion, invoices before posting.</summary>
    Draft = 1,

    /// <summary>Live and awaiting fulfilment: orders and challans from creation.</summary>
    Open = 2,

    /// <summary>Fully carried into successor documents (a quotation converted, a challan invoiced).</summary>
    Converted = 3,

    /// <summary>An order whose ordered quantity is fully delivered or received.</summary>
    Fulfilled = 4,

    /// <summary>A posted invoice: its voucher and stock movements are in the books.</summary>
    Issued = 5,

    /// <summary>Withdrawn before effect. A posted invoice is cancelled through the journal engine,
    /// which reverses rather than deletes — never by flipping this flag alone.</summary>
    Cancelled = 6,
}
