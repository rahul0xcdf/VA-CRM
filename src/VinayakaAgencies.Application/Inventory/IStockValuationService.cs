using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>The costing outcome for one issue quantity.</summary>
/// <param name="UnitCostMinor">
/// Cost of one base unit in minor units of 1/10,000, rounded once to that scale.
/// </param>
/// <param name="Method">The valuation method actually applied.</param>
/// <param name="Warnings">
/// Non-fatal conditions met while costing — consuming more than the layers hold under the
/// negative-stock-warn policy (<c>open-questions.md</c> #20). Empty on an ordinary issue.
/// </param>
public sealed record ValuedIssue(
    long UnitCostMinor,
    ValuationMethod Method,
    IReadOnlyList<string> Warnings)
{
    public static ValuedIssue Clean(long unitCostMinor, ValuationMethod method) =>
        new(unitCostMinor, method, []);
}

/// <summary>
/// Costs issues out of the unified stock ledger (phase-2-inventory.md §2, checkpoint C2.4).
/// </summary>
/// <remarks>
/// Both methods are implemented here and each is pinned by a hand-computed fixture test:
/// <list type="bullet">
/// <item><description><b>FIFO</b> consumes the oldest unreceived-against layers first.</description></item>
/// <item><description><b>Weighted average</b> carries a running (quantity, value) position and costs
/// every issue at its current average.</description></item>
/// </list>
/// Replay walks one item's entries in <see cref="StockLedgerEntry.GlobalSequence"/> order up to the
/// business date, so a voucher is always costed against the state its own posting created — never
/// against a future receipt.
/// </remarks>
public interface IStockValuationService
{
    /// <summary>
    /// Values an issue of <paramref name="quantity"/> base units of an item as of a business date,
    /// using the item's override or the company default.
    /// </summary>
    /// <param name="alreadyIssuedQuantity">
    /// Quantity of the same item issued earlier in the voucher being posted. The replay consumes it
    /// first without charging it, so a second issue line of one item is costed against what is
    /// genuinely left — not against layers its sibling line already took.
    /// </param>
    Task<ValuedIssue> ValueIssueAsync(
        Guid itemId,
        decimal quantity,
        DateOnly asOfDate,
        decimal alreadyIssuedQuantity = 0m,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The valuation method in force for an item: its override, else the company default.
    /// </summary>
    Task<ValuationMethod> EffectiveMethodAsync(
        Guid itemId,
        CancellationToken cancellationToken = default);
}
