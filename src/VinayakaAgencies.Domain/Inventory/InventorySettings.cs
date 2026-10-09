using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// Per-company inventory configuration: the default valuation method
/// (phase-2-inventory.md §2, <c>open-questions.md</c> #19).
/// </summary>
public sealed class InventorySettings : Entity, IAuditStamped
{
    private InventorySettings()
    {
        // EF Core materialisation.
    }

    public InventorySettings(Guid companyId, ValuationMethod defaultValuationMethod)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        DefaultValuationMethod = defaultValuationMethod;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// FIFO at launch. An item without its own override is costed this way.
    /// </summary>
    public ValuationMethod DefaultValuationMethod { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void SetDefaultValuationMethod(ValuationMethod method) =>
        DefaultValuationMethod = method;
}
