using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// A classification node in the chart of accounts. Groups nest; only <see cref="Ledger"/> is postable.
/// </summary>
/// <remarks>
/// Structure is held twice on purpose (spec §4.2): <see cref="ParentGroupId"/> is authoritative, and
/// <see cref="MaterialisedPath"/> is a rebuildable cache of the ancestor chain so a balance roll-up
/// is one indexed range scan instead of a recursive CTE — which would be a second place where SQLite
/// and T-SQL diverge, exactly what ADR-0001 exists to avoid. The path stores GUIDs, not names, so
/// renaming a group does not rewrite the path of every descendant.
/// </remarks>
public sealed class AccountGroup : Entity, IAuditStamped
{
    /// <summary>Deepest nesting allowed, which bounds <see cref="MaterialisedPath"/> to 400 chars.</summary>
    public const int MaxDepth = 10;

    private AccountGroup()
    {
        // EF Core materialisation.
    }

    public AccountGroup(
        Guid companyId,
        string name,
        AccountNature nature,
        Guid? parentGroupId = null,
        bool affectsGrossProfit = false,
        bool isPrimary = false,
        bool isSystem = false,
        int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        Name = name.Trim();
        Nature = nature;
        ParentGroupId = parentGroupId;
        AffectsGrossProfit = affectsGrossProfit;
        IsPrimary = isPrimary;
        IsSystem = isSystem;
        SortOrder = sortOrder;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid? ParentGroupId { get; private set; }

    /// <summary>
    /// Slash-separated chain of ancestor ids in <c>N</c> format, this group last, e.g.
    /// <c>/a1b2…/c3d4…/</c>. Derived from <see cref="ParentGroupId"/> and rebuildable from it.
    /// </summary>
    public string MaterialisedPath { get; private set; } = string.Empty;

    public AccountNature Nature { get; private set; }

    /// <summary>Whether balances here belong above the gross-profit line of the P&amp;L.</summary>
    public bool AffectsGrossProfit { get; private set; }

    /// <summary>One of the fifteen seeded top-level groups.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Seeded and structural: cannot be deleted or re-parented (spec §4.6).</summary>
    public bool IsSystem { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Depth below the root, where a primary group is 1.</summary>
    public int Depth => string.IsNullOrEmpty(MaterialisedPath)
        ? 0
        : MaterialisedPath.Count(c => c == '/') - 1;

    /// <summary>
    /// Recomputes this group's path from its parent's. Callers must apply this to the whole affected
    /// subtree inside one unit of work, or the cache and the parent chain will disagree.
    /// </summary>
    public void SetMaterialisedPath(string? parentPath)
    {
        var prefix = string.IsNullOrEmpty(parentPath) ? "/" : parentPath;
        var candidate = $"{prefix}{Id:N}/";

        if (candidate.Count(c => c == '/') - 1 > MaxDepth)
        {
            throw new InvalidOperationException(
                $"Account groups may not nest deeper than {MaxDepth} levels.");
        }

        MaterialisedPath = candidate;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    /// <summary>
    /// Re-parents this group. The caller is responsible for rebuilding the subtree's paths in the
    /// same transaction.
    /// </summary>
    public void MoveTo(Guid? parentGroupId)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException(
                $"'{Name}' is a system group and cannot be re-parented. Its position is what makes " +
                "the Tally export in Phase 8 map one-to-one.");
        }

        if (parentGroupId == Id)
        {
            throw new InvalidOperationException("A group cannot be its own parent.");
        }

        ParentGroupId = parentGroupId;
    }

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException($"'{Name}' is a system group and cannot be deactivated.");
        }

        IsActive = false;
    }

    public void Activate() => IsActive = true;
}
