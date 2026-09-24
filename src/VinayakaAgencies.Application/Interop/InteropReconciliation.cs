using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Checkpoint C8.6: what the books moved by, against what the file said it carried.
/// </summary>
/// <remarks>
/// <para>
/// §4a D2 added this checkpoint because of the one failure mode a migration has that looks exactly
/// like success: every row imports, no error is reported, and the books are wrong. Nothing upstream
/// can catch it — the parser was happy, the validator was happy, the transaction committed. Only
/// comparing the arithmetic afterwards can.
/// </para>
/// <para>
/// The comparison is made on the <i>change</i> in the books rather than on their absolute totals,
/// because an import lands on top of whatever was already there. The trial balance and the stock
/// valuation are read before the transaction opens and again after it commits, through the same
/// report services the owner reads on screen — deliberately, so this checks the numbers the owner
/// will actually see rather than a private recomputation that could agree with a wrong import.
/// </para>
/// </remarks>
public sealed record InteropReconciliation(
    long ImportedDebitMinor,
    long ImportedCreditMinor,
    long BooksDebitChangeMinor,
    long BooksCreditChangeMinor,
    long ImportedStockValueMinor,
    long BooksStockValueChangeMinor,
    long? DeclaredDebitMinor = null,
    long? DeclaredCreditMinor = null)
{
    /// <summary>The books moved by exactly what the imported vouchers carried.</summary>
    public bool LedgersReconcile =>
        BooksDebitChangeMinor == ImportedDebitMinor && BooksCreditChangeMinor == ImportedCreditMinor;

    /// <summary>Closing stock moved by exactly the value of the imported stock entries.</summary>
    public bool StockReconciles => BooksStockValueChangeMinor == ImportedStockValueMinor;

    /// <summary>
    /// The file's own declared totals agree with what was parsed out of it.
    /// </summary>
    /// <remarks>
    /// True when nothing was declared. A source that says nothing about its totals — which is every
    /// real Tally export, since the format has no such element — cannot disagree with itself, and
    /// reporting "matched" for it would be a claim this has not checked.
    /// </remarks>
    public bool DeclarationReconciles =>
        DeclaredDebitMinor is null
        || (DeclaredDebitMinor == ImportedDebitMinor && DeclaredCreditMinor == ImportedCreditMinor);

    /// <summary>Whether the source declared totals at all, so a report can distinguish the two cases.</summary>
    public bool HasDeclaration => DeclaredDebitMinor is not null;

    public bool IsClean => LedgersReconcile && StockReconciles && DeclarationReconciles;

    public long DebitDifferenceMinor => BooksDebitChangeMinor - ImportedDebitMinor;

    public long CreditDifferenceMinor => BooksCreditChangeMinor - ImportedCreditMinor;

    public long StockDifferenceMinor => BooksStockValueChangeMinor - ImportedStockValueMinor;

    /// <summary>A line the owner can read, naming the difference rather than just flagging one.</summary>
    public string Describe()
    {
        if (IsClean)
        {
            return "The books moved by exactly what the file carried: "
                + $"debit {MoneyScale.ToInvariantString(MoneyScale.FromMinor(ImportedDebitMinor))}, "
                + $"credit {MoneyScale.ToInvariantString(MoneyScale.FromMinor(ImportedCreditMinor))}, "
                + $"stock {MoneyScale.ToInvariantString(MoneyScale.FromMinor(ImportedStockValueMinor))}.";
        }

        var differences = new List<string>(3);

        if (!LedgersReconcile)
        {
            differences.Add(
                $"the trial balance moved by {Money(BooksDebitChangeMinor)} debit / "
                + $"{Money(BooksCreditChangeMinor)} credit, but the file carried "
                + $"{Money(ImportedDebitMinor)} / {Money(ImportedCreditMinor)}");
        }

        if (!StockReconciles)
        {
            differences.Add(
                $"closing stock moved by {Money(BooksStockValueChangeMinor)}, but the file carried "
                + $"{Money(ImportedStockValueMinor)}");
        }

        if (!DeclarationReconciles)
        {
            differences.Add(
                $"the file declares totals of {Money(DeclaredDebitMinor ?? 0)} / "
                + $"{Money(DeclaredCreditMinor ?? 0)}, which is not what was parsed out of it");
        }

        return "The import does not reconcile: " + string.Join("; ", differences) + ".";
    }

    private static string Money(long minor) =>
        MoneyScale.ToInvariantString(MoneyScale.FromMinor(minor));
}
