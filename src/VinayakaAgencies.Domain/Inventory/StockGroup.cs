using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// A classification of items for reporting and filtering — the inventory counterpart of an
/// account group, flat rather than hierarchical in this phase.
/// </summary>
public sealed class StockGroup : Entity, IAuditStamped
{
    private StockGroup()
    {
        // EF Core materialisation.
    }

    public StockGroup(Guid companyId, string name, string? description = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CompanyId = companyId;
        SetName(name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, over which the unique <c>(CompanyId, NameNormalised)</c>
    /// index is built. See <see cref="NameNormaliser"/>.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetName(name);
    }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
