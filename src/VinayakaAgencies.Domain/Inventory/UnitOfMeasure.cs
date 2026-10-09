using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// A unit of measure items are stocked and billed in — "Nos", "Box", "Set".
/// </summary>
/// <remarks>
/// Every stock quantity in the ledger is held in the item's base unit. An item's alternate unit
/// with its conversion factor lives on <see cref="Item"/> (phase-2-inventory.md §2): it exists so
/// Phase 3 can buy in one unit and sell in another without re-keying, not because the ledger ever
/// stores anything but base units.
/// </remarks>
public sealed class UnitOfMeasure : Entity, IAuditStamped
{
    private UnitOfMeasure()
    {
        // EF Core materialisation.
    }

    public UnitOfMeasure(Guid companyId, string code, int decimalPlaces = 0, string? description = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (decimalPlaces is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalPlaces),
                decimalPlaces,
                "Decimal places must be between 0 and 4, matching the quantity scale.");
        }

        CompanyId = companyId;
        SetCode(code);
        DecimalPlaces = decimalPlaces;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Short code as operators type it — "NOS", "PCS". Unique per company.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Code"/>, over which the unique <c>(CompanyId, CodeNormalised)</c>
    /// index is built.
    /// </summary>
    public string CodeNormalised { get; private set; } = string.Empty;

    /// <summary>Fractional digits quantities in this unit may carry (0–4).</summary>
    public int DecimalPlaces { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void Rename(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        SetCode(code);
    }

    public void SetDecimalPlaces(int decimalPlaces)
    {
        if (decimalPlaces is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalPlaces),
                decimalPlaces,
                "Decimal places must be between 0 and 4, matching the quantity scale.");
        }

        DecimalPlaces = decimalPlaces;
    }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void SetCode(string code)
    {
        Code = code.Trim();
        CodeNormalised = NameNormaliser.Normalise(code);
    }
}
