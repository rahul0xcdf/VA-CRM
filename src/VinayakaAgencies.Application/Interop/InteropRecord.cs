namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// One record read from an import source, carrying where it came from.
/// </summary>
/// <remarks>
/// Sources yield these lazily so a full-history file is never held in memory at once (§4a D2). The
/// closed set of nested cases is deliberate: adding a master kind should fail to compile every
/// switch that has to handle it, rather than silently skipping the new kind at import time.
/// </remarks>
public abstract record InteropRecord
{
    private protected InteropRecord(InteropSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
    }

    public InteropSourceLocation Location { get; }

    /// <summary>The record's kind, for counting and for grouping the error report.</summary>
    public abstract string Kind { get; }

    /// <summary>The record's own name, for an error message that identifies it to a human.</summary>
    public abstract string Label { get; }

    public sealed record AccountGroup(InteropSourceLocation Location, InteropAccountGroup Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Account group";

        public override string Label => Value.Name;
    }

    public sealed record Ledger(InteropSourceLocation Location, InteropLedger Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Ledger";

        public override string Label => Value.Name;
    }

    public sealed record Unit(InteropSourceLocation Location, InteropUnit Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Unit";

        public override string Label => Value.Code;
    }

    public sealed record StockGroup(InteropSourceLocation Location, InteropStockGroup Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Stock group";

        public override string Label => Value.Name;
    }

    public sealed record Godown(InteropSourceLocation Location, InteropGodown Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Godown";

        public override string Label => Value.Name;
    }

    public sealed record Item(InteropSourceLocation Location, InteropItem Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Stock item";

        public override string Label => Value.Name;
    }

    public sealed record VoucherType(InteropSourceLocation Location, InteropVoucherType Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Voucher type";

        public override string Label => Value.Name;
    }

    public sealed record Voucher(InteropSourceLocation Location, InteropVoucher Value)
        : InteropRecord(Location)
    {
        public override string Kind => "Voucher";

        public override string Label =>
            string.IsNullOrWhiteSpace(Value.Number) ? Value.VoucherTypeName : Value.Number;
    }

    /// <summary>
    /// A record the source could not parse at all.
    /// </summary>
    /// <remarks>
    /// Malformed input becomes a record rather than an exception, because §2 requires the whole file
    /// to be validated before anything is written: throwing on the first bad element would hand the
    /// owner one problem at a time out of a file that may have twenty.
    /// </remarks>
    public sealed record Unreadable(InteropSourceLocation Location, string Reason, string SourceKind)
        : InteropRecord(Location)
    {
        public override string Kind => SourceKind;

        public override string Label => Reason;
    }
}
