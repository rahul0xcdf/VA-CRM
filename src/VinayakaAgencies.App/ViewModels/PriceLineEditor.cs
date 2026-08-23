using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// One editable row of a price table — a category's price list or a customer's special prices.
/// The value cell reads as rupees on a fixed-price line and as a percent otherwise
/// (phase-4-customer-pricing.md §2).
/// </summary>
public sealed partial class PriceLineEditor : ObservableObject
{
    public PriceLineEditor(
        Guid? id,
        Guid itemId,
        string itemName,
        PriceBasis basis,
        decimal? fixedPrice,
        decimal? marginPercent,
        bool isDirty = false)
    {
        Id = id;
        ItemId = itemId;
        _itemName = itemName;
        _basis = basis;
        _valueText = (fixedPrice ?? marginPercent)?
            .ToString("0.##", CultureInfo.InvariantCulture) ?? "0";
        IsDirty = isDirty;
    }

    /// <summary>Null on a row not yet saved.</summary>
    public Guid? Id { get; }

    public Guid ItemId { get; }

    [ObservableProperty]
    private string _itemName;

    [ObservableProperty]
    private PriceBasis _basis;

    [ObservableProperty]
    private string _valueText;

    /// <summary>True when the row needs saving — new rows start dirty, loaded rows turn dirty on edit.</summary>
    public bool IsDirty { get; private set; }

    partial void OnValueTextChanged(string value) => IsDirty = true;

    partial void OnBasisChanged(PriceBasis value) => IsDirty = true;

    /// <summary>The parsed value as a fixed price, when the basis is <see cref="PriceBasis.FixedPrice"/>.</summary>
    public decimal? ParsedFixedPrice =>
        Basis == PriceBasis.FixedPrice && decimal.TryParse(ValueText, out var price)
            ? Math.Round(price, 4, MidpointRounding.ToEven)
            : null;

    /// <summary>The parsed value as a margin/discount percent, when the basis is a margin.</summary>
    public decimal? ParsedMarginPercent =>
        Basis != PriceBasis.FixedPrice && decimal.TryParse(ValueText, out var percent)
            ? Math.Round(percent, 2, MidpointRounding.ToEven)
            : null;

    public void MarkClean() => IsDirty = false;
}
