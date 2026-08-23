using System.Globalization;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// The FIFO and weighted-average costing engines over the unified stock ledger
/// (phase-2-inventory.md §2, checkpoint C2.4).
/// </summary>
public sealed class StockValuationService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IStockValuationService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<ValuationMethod> EffectiveMethodAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var item = await RequireItemAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);

        if (item.ValuationOverride.HasValue)
        {
            return item.ValuationOverride.Value;
        }

        var settings = await EnsureSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        return settings.DefaultValuationMethod;
    }

    /// <inheritdoc />
    public async Task<ValuedIssue> ValueIssueAsync(
        Guid itemId,
        decimal quantity,
        DateOnly asOfDate,
        decimal alreadyIssuedQuantity = 0m,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Only a positive quantity can be valued.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(alreadyIssuedQuantity);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var item = await RequireItemAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);
        var method = item.ValuationOverride
            ?? (await EnsureSettingsAsync(companyId, cancellationToken).ConfigureAwait(false))
            .DefaultValuationMethod;

        var entries = await _unitOfWork.Repository<StockLedgerEntry>()
            .ListAsync(new ItemEntriesUpTo(companyId, itemId, asOfDate), cancellationToken)
            .ConfigureAwait(false);

        return method == ValuationMethod.Fifo
            ? ValueFifo(entries, quantity, alreadyIssuedQuantity, method)
            : ValueWeightedAverage(entries, quantity, alreadyIssuedQuantity, method);
    }

    /// <summary>
    /// FIFO: replay the item's entries in posting order keeping the unconsumed layers, then consume
    /// the requested quantity oldest-first.
    /// </summary>
    private static ValuedIssue ValueFifo(
        IReadOnlyList<StockLedgerEntry> entries,
        decimal quantity,
        decimal alreadyIssuedQuantity,
        ValuationMethod method)
    {
        var layers = new LinkedList<(decimal Quantity, long UnitCostMinor)>();
        var warnings = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.Direction == StockMovementDirection.Increase)
            {
                layers.AddLast((entry.QuantityIn, entry.UnitCostMinor));
                continue;
            }

            Consume(layers, entry.QuantityOut);
        }

        // Earlier lines of this same voucher took their share first; drop it from the front of the
        // layers without charging it to anybody.
        Consume(layers, alreadyIssuedQuantity);

        var remaining = quantity;
        long valueMinor = 0;
        long lastKnownCostMinor = 0;

        while (remaining > 0m && layers.Count > 0)
        {
            var layer = layers.First!.Value;
            var taken = decimal.Min(layer.Quantity, remaining);

            valueMinor += StockLedgerEntry.ComputeValueMinor(taken, layer.UnitCostMinor);
            lastKnownCostMinor = layer.UnitCostMinor;
            remaining -= taken;

            layers.RemoveFirst();
            if (layer.Quantity > taken)
            {
                layers.AddFirst((layer.Quantity - taken, layer.UnitCostMinor));
            }
        }

        if (remaining > 0m)
        {
            // Approved warn policy (open-questions.md #20): the shortfall prices at the last known
            // cost rather than diluting the average across quantity that was never on the shelf.
            valueMinor += StockLedgerEntry.ComputeValueMinor(remaining, lastKnownCostMinor);
            warnings.Add(
                $"Valuation warning: {quantity.ToString("0.####", CultureInfo.InvariantCulture)} base "
                + "unit(s) exceeded the cost layers on hand under FIFO; the shortfall of "
                + $"{remaining.ToString("0.####", CultureInfo.InvariantCulture)} is carried at the "
                + "last known cost.");
        }

        return new ValuedIssue(UnitCostFromValue(valueMinor, quantity), method, warnings);
    }

    /// <summary>
    /// Weighted average: carry a running (quantity, value) position through the replay and cost the
    /// issue at its current average.
    /// </summary>
    private static ValuedIssue ValueWeightedAverage(
        IReadOnlyList<StockLedgerEntry> entries,
        decimal quantity,
        decimal alreadyIssuedQuantity,
        ValuationMethod method)
    {
        var warnings = new List<string>();

        decimal onHand = 0m;
        long valueMinor = 0;
        long lastUnitCostMinor = 0;

        foreach (var entry in entries)
        {
            if (entry.Direction == StockMovementDirection.Increase)
            {
                onHand += entry.QuantityIn;
                valueMinor += entry.ValueMinor;
                lastUnitCostMinor = entry.UnitCostMinor;
                continue;
            }

            var issued = entry.QuantityOut;

            if (onHand <= 0m)
            {
                // History already shows an issue without stock to value it from; keep walking with
                // the last known cost so later averages are not poisoned by a zero.
                continue;
            }

            var unitCost = UnitCostFromValue(valueMinor, onHand);
            valueMinor -= StockLedgerEntry.ComputeValueMinor(issued, unitCost);
            onHand -= issued;

            if (onHand < 0m)
            {
                // A historical negative: clamp the position rather than carrying negative value
                // into every later average.
                onHand = 0m;
                valueMinor = 0;
            }
            else if (valueMinor < 0)
            {
                valueMinor = 0;
            }
        }

        // Earlier lines of this same voucher already walked out of the average; step past them
        // without charging, at the current average cost.
        if (alreadyIssuedQuantity > 0m && onHand > 0m)
        {
            var unitCost = UnitCostFromValue(valueMinor, onHand);
            valueMinor -= StockLedgerEntry.ComputeValueMinor(alreadyIssuedQuantity, unitCost);
            onHand -= alreadyIssuedQuantity;

            if (onHand < 0m)
            {
                onHand = 0m;
                valueMinor = 0;
            }
            else if (valueMinor < 0)
            {
                valueMinor = 0;
            }
        }

        long valuedMinor;

        if (onHand >= quantity)
        {
            valuedMinor = StockLedgerEntry.ComputeValueMinor(quantity, UnitCostFromValue(valueMinor, onHand));
        }
        else
        {
            // Approved warn policy (open-questions.md #20): everything on the shelf prices at the
            // running average, the shortfall at the last known receipt cost.
            var shortfall = quantity - onHand;
            valuedMinor = onHand > 0m
                ? StockLedgerEntry.ComputeValueMinor(onHand, UnitCostFromValue(valueMinor, onHand))
                : 0L;
            valuedMinor += StockLedgerEntry.ComputeValueMinor(shortfall, lastUnitCostMinor);

            warnings.Add(
                $"Valuation warning: only {onHand.ToString("0.####", CultureInfo.InvariantCulture)} "
                + "base unit(s) were on hand under weighted average; the shortfall of "
                + $"{shortfall.ToString("0.####", CultureInfo.InvariantCulture)} is carried at the "
                + "last known cost.");
        }

        return new ValuedIssue(UnitCostFromValue(valuedMinor, quantity), method, warnings);
    }

    /// <summary>Removes a quantity from the front of the layer list.</summary>
    private static void Consume(LinkedList<(decimal Quantity, long UnitCostMinor)> layers, decimal quantity)
    {
        var remaining = quantity;

        while (remaining > 0m && layers.Count > 0)
        {
            var layer = layers.First!.Value;
            var taken = decimal.Min(layer.Quantity, remaining);

            remaining -= taken;

            layers.RemoveFirst();
            if (layer.Quantity > taken)
            {
                layers.AddFirst((layer.Quantity - taken, layer.UnitCostMinor));
            }
        }
    }

    /// <summary>
    /// Derives one base-unit cost from a total value, in minor units, rounding once to scale.
    /// </summary>
    private static long UnitCostFromValue(long valueMinor, decimal quantity) =>
        StockLedgerEntry.UnitCostFromValue(valueMinor, quantity);

    private async Task<Item> RequireItemAsync(
        Guid companyId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<Item>()
            .GetByIdAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                $"Item '{itemId}' does not exist in this company's books.");
        }

        return item;
    }

    /// <summary>
    /// Reads the settings row, creating it at the approved default (FIFO) when absent — the same
    /// lazy-but-audited pattern as any first write through the unit of work.
    /// </summary>
    private async Task<InventorySettings> EnsureSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var settings = await _unitOfWork.Repository<InventorySettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (settings is not null)
        {
            return settings;
        }

        settings = new InventorySettings(companyId, ValuationMethod.Fifo);
        await _unitOfWork.Repository<InventorySettings>()
            .AddAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return settings;
    }

    private sealed class ItemEntriesUpTo : Specification<StockLedgerEntry>
    {
        public ItemEntriesUpTo(Guid companyId, Guid itemId, DateOnly asOfDate)
        {
            Where(entry => entry.CompanyId == companyId
                && entry.ItemId == itemId
                && entry.EntryDate <= asOfDate);
            ApplyOrderBy(entry => entry.GlobalSequence);
            ApplyNoTracking();
        }
    }

    private sealed class SettingsOfCompany : Specification<InventorySettings>
    {
        public SettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
