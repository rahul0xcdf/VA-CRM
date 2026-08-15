using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// An optional dimension on a journal line, e.g. a branch or a salesperson.
/// </summary>
/// <remarks>
/// Owner decision D3: schema in Phase 1, no UI. Nests the same way <see cref="AccountGroup"/> does,
/// for the same reason — see spec §4.2.
/// </remarks>
public sealed class CostCentre : Entity, IAuditStamped
{
    public const int MaxDepth = 10;

    private CostCentre()
    {
        // EF Core materialisation.
    }

    public CostCentre(
        Guid companyId,
        string name,
        Guid? costCategoryId = null,
        Guid? parentCostCentreId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        SetName(name);
        CostCategoryId = costCategoryId;
        ParentCostCentreId = parentCostCentreId;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, which the unique name index is built over so that two
    /// cost centres cannot differ only by letter case. See <see cref="NameNormaliser"/>.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public Guid? CostCategoryId { get; private set; }

    public Guid? ParentCostCentreId { get; private set; }

    public string MaterialisedPath { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void SetMaterialisedPath(string? parentPath)
    {
        var prefix = string.IsNullOrEmpty(parentPath) ? "/" : parentPath;
        var candidate = $"{prefix}{Id:N}/";

        if (candidate.Count(c => c == '/') - 1 > MaxDepth)
        {
            throw new InvalidOperationException(
                $"Cost centres may not nest deeper than {MaxDepth} levels.");
        }

        MaterialisedPath = candidate;
    }

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
