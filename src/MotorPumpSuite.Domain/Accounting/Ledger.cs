using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// A postable account. Journal lines reference a ledger, never an <see cref="AccountGroup"/>.
/// </summary>
/// <remarks>
/// There is deliberately **no opening-balance column** (spec §8.5). An opening balance is posted as
/// a dated journal voucher against "Opening Balance Difference", because a writable balance column
/// would be a stored balance in all but name — and <c>project.md</c> §4.1 requires that balances are
/// always derived.
/// </remarks>
public sealed class Ledger : Entity, IAuditStamped
{
    private Ledger()
    {
        // EF Core materialisation.
    }

    public Ledger(
        Guid companyId,
        string name,
        Guid accountGroupId,
        string? alias = null,
        bool isSystem = false,
        bool allowsManualPosting = true,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (accountGroupId == Guid.Empty)
        {
            throw new ArgumentException("A ledger must belong to a group.", nameof(accountGroupId));
        }

        CompanyId = companyId;
        Name = name.Trim();
        AccountGroupId = accountGroupId;
        Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        IsSystem = isSystem;
        AllowsManualPosting = allowsManualPosting;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Short code or alternate name, used by the Phase 2 fast-search requirement.</summary>
    public string? Alias { get; private set; }

    public Guid AccountGroupId { get; private set; }

    /// <summary>Seeded and structural: cannot be deleted (spec §4.5).</summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// Whether a user may post to this ledger directly. False for "Profit &amp; Loss A/c", whose
    /// balance must always be derived from income and expense rather than written to.
    /// </summary>
    public bool AllowsManualPosting { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void Rename(string name, string? alias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (IsSystem && !AllowsManualPosting)
        {
            throw new InvalidOperationException(
                $"'{Name}' is fully locked and cannot be renamed.");
        }

        Name = name.Trim();
        Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
    }

    public void MoveTo(Guid accountGroupId)
    {
        if (accountGroupId == Guid.Empty)
        {
            throw new ArgumentException("A ledger must belong to a group.", nameof(accountGroupId));
        }

        if (IsSystem)
        {
            throw new InvalidOperationException(
                $"'{Name}' is a system ledger and cannot be moved to another group.");
        }

        AccountGroupId = accountGroupId;
    }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException($"'{Name}' is a system ledger and cannot be deactivated.");
        }

        IsActive = false;
    }

    public void Activate() => IsActive = true;
}
