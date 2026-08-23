using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// A customer classification — "Retailer", "Dealer", "Wholesaler", … — whose price list prices the
/// customers inside it (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Categories are master data rows, not a code enum (<c>open-questions.md</c> #2 as closed on
/// approval, §4a): the four seeded placeholders are freely renamable and extendable, and nothing
/// about them is hardcoded.
/// <para>
/// Every customer belongs to exactly one category. Selecting a customer on a sale document is what
/// auto-applies that category's price or margin, so a customer's category is mandatory on
/// <see cref="Customer"/>.
/// </para>
/// </remarks>
public sealed class CustomerCategory : Entity, IAuditStamped
{
    private CustomerCategory()
    {
        // EF Core materialisation.
    }

    public CustomerCategory(Guid companyId, string name, string? description = null)
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
    /// Comparison form of <see cref="Name"/> — the unique <c>(CompanyId, NameNormalised)</c> index is
    /// built over it, exactly as for <see cref="Accounting.Ledger"/> and every other master. See
    /// <see cref="NameNormaliser"/>.
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

    /// <summary>Keeps <see cref="NameNormalised"/> in step with <see cref="Name"/>; the two are only
    /// ever written together.</summary>
    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
