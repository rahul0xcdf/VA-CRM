using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// A godown (warehouse / location) stock is held in (phase-2-inventory.md §2).
/// </summary>
/// <remarks>
/// On-hand quantity per godown is always derived by summing the unified stock ledger — there is no
/// stored quantity anywhere. The seeded system row "Main Location" gives a fresh install a place to
/// receive into without a setup detour; it cannot be deleted or deactivated, only renamed.
/// </remarks>
public sealed class Godown : Entity, IAuditStamped
{
    /// <summary>Name of the seeded system godown.</summary>
    public const string MainLocationName = "Main Location";

    private Godown()
    {
        // EF Core materialisation.
    }

    public Godown(Guid companyId, string name, string? address = null, bool isSystem = false)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CompanyId = companyId;
        SetName(name);
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        IsSystem = isSystem;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, over which the unique <c>(CompanyId, NameNormalised)</c>
    /// index is built. See <see cref="NameNormaliser"/>.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    /// <summary>Seeded rows cannot be deleted or deactivated (spec §4.6 conventions).</summary>
    public bool IsSystem { get; private set; }

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

    public void SetAddress(string? address) =>
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException(
                $"'{Name}' is the seeded system godown and cannot be deactivated.");
        }

        IsActive = false;
    }

    public void Activate() => IsActive = true;

    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
