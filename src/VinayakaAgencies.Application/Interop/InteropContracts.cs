using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// The vendor-neutral shape that every interop path reads and writes.
/// </summary>
/// <remarks>
/// <para>
/// One model, read in both directions (phase-8-interoperability.md §4a D3). Export builds this from
/// the books and hands it to a writer; import parses a file into this and hands it to one validator
/// and one committer. Tally XML, Tally JSON and Excel are <i>serialisations</i> of this model, not
/// three pipelines — which is what makes the round-trip checkpoint C8.2 meaningful rather than a
/// coincidence between an exporter and an importer that happen to agree.
/// </para>
/// <para>
/// Every amount and quantity is carried as scaled integer minor units (ADR-0003), converted to
/// decimal exactly once inside a serialiser. C8.5 — no precision loss across a round-trip — is a
/// consequence of that rather than something the writers have to be careful about.
/// </para>
/// </remarks>
public sealed record InteropDocument
{
    /// <summary>The company these records belong to, by name. Tally identifies a company by name.</summary>
    public required string CompanyName { get; init; }

    public string? CompanyGstin { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Start of the voucher window, when the document carries transactions.</summary>
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public IReadOnlyList<InteropAccountGroup> AccountGroups { get; init; } = [];

    public IReadOnlyList<InteropLedger> Ledgers { get; init; } = [];

    public IReadOnlyList<InteropUnit> Units { get; init; } = [];

    public IReadOnlyList<InteropStockGroup> StockGroups { get; init; } = [];

    public IReadOnlyList<InteropGodown> Godowns { get; init; } = [];

    public IReadOnlyList<InteropItem> Items { get; init; } = [];

    public IReadOnlyList<InteropVoucherType> VoucherTypes { get; init; } = [];

    public IReadOnlyList<InteropVoucher> Vouchers { get; init; } = [];

    /// <summary>
    /// What the source claims its own totals are, when it says so.
    /// </summary>
    /// <remarks>
    /// This is what C8.6 reconciles against after an import commits. A source that declares nothing
    /// leaves this null and the reconciliation reports "not declared" rather than "matched" — a
    /// silent pass on an undeclared total would be exactly the false reassurance §4a D2 warns about.
    /// </remarks>
    public InteropTotals? DeclaredTotals { get; init; }

    public bool HasMasters =>
        AccountGroups.Count > 0
        || Ledgers.Count > 0
        || Units.Count > 0
        || StockGroups.Count > 0
        || Godowns.Count > 0
        || Items.Count > 0
        || VoucherTypes.Count > 0;

    public bool IsEmpty => !HasMasters && Vouchers.Count == 0;
}

/// <summary>Totals a source declares about itself, for post-import reconciliation (C8.6).</summary>
public sealed record InteropTotals(
    long TotalDebitMinor,
    long TotalCreditMinor,
    long? ClosingStockValueMinor = null)
{
    /// <summary>A set of books that does not balance cannot be a correct export of anything.</summary>
    public bool IsBalanced => TotalDebitMinor == TotalCreditMinor;
}

/// <summary>An account group. <paramref name="ParentName"/> is null at the top of the tree.</summary>
public sealed record InteropAccountGroup(
    string Name,
    string? ParentName,
    AccountNature Nature,
    Guid? Id = null,
    bool AffectsGrossProfit = false,
    bool IsActive = true);

/// <summary>A ledger account, filed under an account group by name.</summary>
public sealed record InteropLedger(
    string Name,
    string ParentGroupName,
    Guid? Id = null,
    string? Alias = null,
    string? Description = null,
    string? Gstin = null,
    bool IsActive = true);

/// <summary>A unit of measure. <paramref name="Code"/> is the symbol Tally prints against a quantity.</summary>
public sealed record InteropUnit(
    string Code,
    int DecimalPlaces = 0,
    Guid? Id = null,
    string? Description = null,
    bool IsActive = true);

public sealed record InteropStockGroup(
    string Name,
    Guid? Id = null,
    string? Description = null,
    bool IsActive = true);

public sealed record InteropGodown(
    string Name,
    Guid? Id = null,
    string? Address = null,
    bool IsActive = true);

/// <summary>
/// A voucher type: the document kind the owner files under, and the behaviour it posts with.
/// </summary>
/// <remarks>
/// Carried as a master because a company's voucher types are its own. "Stock Journal", "Cash Sales",
/// "Bank Payment" — none of them is a name Tally ships with, and a file that named one without
/// defining it would leave the reader guessing which of the eight behaviours it posts with. Guessing
/// wrong sends real money down the wrong path, so the file says.
/// </remarks>
/// <param name="NumberPrefix">The series prefix, so a type created on import numbers the way it did before.</param>
public sealed record InteropVoucherType(
    string Name,
    VoucherKind Kind,
    Guid? Id = null,
    string? NumberPrefix = null,
    bool IsActive = true);

/// <summary>A stock item, filed under a stock group and measured in a base unit, both by name.</summary>
public sealed record InteropItem(
    string Name,
    string BaseUnitCode,
    Guid? Id = null,
    string? Code = null,
    string? StockGroupName = null,
    string? HsnSacCode = null,
    bool IsActive = true);

/// <summary>
/// One voucher and everything it posts.
/// </summary>
/// <remarks>
/// A cancelled voucher is carried with <see cref="IsCancelled"/> set and no lines, rather than
/// omitted. The journal is append-only (ADR-0002) and its numbers are gapless by design: dropping a
/// cancelled voucher from an export would present the accountant with a numbering gap that does not
/// exist in the books.
/// </remarks>
public sealed record InteropVoucher
{
    /// <summary>The stable entity GUID. Tally overwrites by GUID, so this is what stops a re-import duplicating.</summary>
    public Guid? Id { get; init; }

    public required string VoucherTypeName { get; init; }

    /// <summary>
    /// The posting behaviour, when the type name alone settles it.
    /// </summary>
    /// <remarks>
    /// Null for a type name the reader does not recognise, which is the normal case for a
    /// user-defined type. The importer resolves it from the <see cref="InteropVoucherType"/> master
    /// in the same file rather than inferring it here — see that type's remarks.
    /// </remarks>
    public VoucherKind? Kind { get; init; }

    public required DateOnly Date { get; init; }

    public required string Number { get; init; }

    public string? Narration { get; init; }

    public string? ReferenceNumber { get; init; }

    public DateOnly? ReferenceDate { get; init; }

    public bool IsCancelled { get; init; }

    public IReadOnlyList<InteropLedgerEntry> LedgerEntries { get; init; } = [];

    public IReadOnlyList<InteropInventoryEntry> InventoryEntries { get; init; } = [];

    /// <summary>Signed sum of the ledger entries; zero on a balanced voucher.</summary>
    public long NetMinor => LedgerEntries.Sum(entry => entry.AmountMinor);

    public bool IsBalanced => NetMinor == 0;

    public long TotalDebitMinor =>
        LedgerEntries.Where(entry => entry.AmountMinor > 0).Sum(entry => entry.AmountMinor);

    public long TotalCreditMinor =>
        -LedgerEntries.Where(entry => entry.AmountMinor < 0).Sum(entry => entry.AmountMinor);
}

/// <summary>
/// One posting to a ledger.
/// </summary>
/// <param name="AmountMinor">
/// Signed in <i>this application's</i> convention — positive debit, negative credit, matching
/// <c>JournalLine.AmountMinor</c>. Tally's file format uses the opposite sign; that inversion lives
/// in <see cref="TallyConventions"/> and nowhere else.
/// </param>
public sealed record InteropLedgerEntry(
    string LedgerName,
    long AmountMinor,
    string? CostCentreName = null,
    string? Narration = null)
{
    public bool IsDebit => AmountMinor > 0;
}

/// <summary>
/// One stock movement carried on a voucher.
/// </summary>
/// <param name="QuantityMinor">Signed: positive inward, negative outward.</param>
public sealed record InteropInventoryEntry(
    string StockItemName,
    long QuantityMinor,
    long AmountMinor,
    string? GodownName = null,
    string? UnitCode = null,
    long? RateMinor = null,
    string? BatchName = null);
