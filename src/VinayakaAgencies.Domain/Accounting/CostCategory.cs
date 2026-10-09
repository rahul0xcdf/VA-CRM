using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// A grouping of cost centres, e.g. "Branch" or "Salesperson".
/// </summary>
/// <remarks>
/// Owner decision D3: the schema ships in Phase 1 with no UI. Adding a dimension to a populated
/// <see cref="JournalLine"/> table later is a far more expensive migration than carrying an unused
/// nullable column now.
/// </remarks>
public sealed class CostCategory : Entity, IAuditStamped
{
    private CostCategory()
    {
        // EF Core materialisation.
    }

    public CostCategory(Guid companyId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        SetName(name);
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, which the unique name index is built over so that two
    /// categories cannot differ only by letter case. See <see cref="NameNormaliser"/>.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetName(name);
    }

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
