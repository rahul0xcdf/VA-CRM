namespace VinayakaAgencies.Application.Interop;

/// <summary>The file formats an export can be written as.</summary>
public enum InteropFormat
{
    /// <summary>Tally's XML "Data Interchange" envelope. The format C8.1 is proved against.</summary>
    TallyXml = 1,

    /// <summary>The same envelope as JSON, for TallyPrime 7.0 and later (§4a D1).</summary>
    TallyJson = 2,

    /// <summary>A workbook, one sheet per record kind.</summary>
    Excel = 3,
}

/// <summary>What to put in an export.</summary>
/// <remarks>
/// Masters and vouchers are separately selectable because the two are wanted at different times: the
/// accountant gets vouchers every month, and the masters only when something has been added to them.
/// A voucher window with no dates means the whole book.
/// </remarks>
public sealed record InteropExportRequest(
    bool IncludeMasters = true,
    bool IncludeVouchers = true,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
{
    /// <summary>Masters only — the shape wanted when setting a fresh Tally company up.</summary>
    public static InteropExportRequest MastersOnly() => new(IncludeMasters: true, IncludeVouchers: false);

    /// <summary>One period's transactions, with the masters they name.</summary>
    public static InteropExportRequest Period(DateOnly from, DateOnly to) => new(true, true, from, to);
}

/// <summary>What an export produced, for the screen and for the evidence file.</summary>
public sealed record InteropExportSummary(
    InteropFormat Format,
    int AccountGroupCount,
    int LedgerCount,
    int UnitCount,
    int StockGroupCount,
    int GodownCount,
    int ItemCount,
    int VoucherTypeCount,
    int VoucherCount,
    long TotalDebitMinor,
    long TotalCreditMinor)
{
    public int MasterCount =>
        AccountGroupCount + LedgerCount + UnitCount + StockGroupCount + GodownCount + ItemCount
        + VoucherTypeCount;

    public int RecordCount => MasterCount + VoucherCount;

    /// <summary>
    /// Whether the exported vouchers balance among themselves.
    /// </summary>
    /// <remarks>
    /// An export that does not balance is not a Tally problem waiting to happen — it is evidence
    /// that the selection dropped a line, since every voucher in the books balances by construction.
    /// </remarks>
    public bool IsBalanced => TotalDebitMinor == TotalCreditMinor;

    public static InteropExportSummary For(InteropDocument document, InteropFormat format)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new InteropExportSummary(
            format,
            document.AccountGroups.Count,
            document.Ledgers.Count,
            document.Units.Count,
            document.StockGroups.Count,
            document.Godowns.Count,
            document.Items.Count,
            document.VoucherTypes.Count,
            document.Vouchers.Count,
            document.Vouchers.Sum(voucher => voucher.TotalDebitMinor),
            document.Vouchers.Sum(voucher => voucher.TotalCreditMinor));
    }
}
