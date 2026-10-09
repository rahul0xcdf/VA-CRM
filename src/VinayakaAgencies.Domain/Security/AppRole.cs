using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Security;

/// <summary>
/// A named set of <see cref="PermissionKind"/> grants that users are assigned to.
/// </summary>
/// <remarks>
/// A data row rather than an enum, per `phase-9-multiuser.md` §4a D4: the owner chose Owner and
/// Operator for launch, and adding an Accountant later has to be master data, not a release.
/// <para>
/// <b><see cref="IsSystem"/> is not "cannot be edited".</b> It marks the two roles the product
/// seeds so they cannot be deleted out from under the users assigned to them, and it protects
/// <see cref="IsOwnerRole"/>'s grants specifically — see <see cref="Revoke"/>. Everything else about
/// a system role, including its name and its other grants, stays the owner's to change, because a
/// role the owner cannot correct is the enum D4 rejected wearing a table.
/// </para>
/// </remarks>
public sealed class AppRole : Entity, IAuditStamped
{
    private readonly List<RolePermissionGrant> _grants = [];

    private AppRole()
    {
        // EF Core materialisation.
    }

    public AppRole(Guid companyId, string name, bool isSystem = false, bool isOwnerRole = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (isOwnerRole && !isSystem)
        {
            throw new ArgumentException(
                "The owner role is seeded by the product and must be a system role.",
                nameof(isOwnerRole));
        }

        CompanyId = companyId;
        SetName(name);
        IsSystem = isSystem;
        IsOwnerRole = isOwnerRole;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, which the unique <c>(CompanyId, NameNormalised)</c>
    /// index is built over so a role name means the same thing on both providers.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Seeded by the product, so it cannot be deleted.</summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// The role that holds every permission, now and after a later release adds more.
    /// </summary>
    /// <remarks>
    /// Stored as a flag rather than inferred from "holds all current permissions", because those
    /// two drift apart the moment a permission is added: the owner's role would silently stop
    /// being able to do the new thing, and the first person to notice would be an owner locked out
    /// of a feature they paid for. <see cref="Grants"/> is still populated for this role so the
    /// user-admin screen can show what it covers.
    /// </remarks>
    public bool IsOwnerRole { get; private set; }

    public IReadOnlyCollection<RolePermissionGrant> Grants => _grants.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(Name);
    }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    /// <summary>Whether this role carries <paramref name="permission"/>.</summary>
    public bool Allows(PermissionKind permission) =>
        IsOwnerRole || _grants.Any(grant => grant.Permission == permission);

    /// <summary>Adds <paramref name="permission"/>, or does nothing if it is already granted.</summary>
    public void Grant(PermissionKind permission)
    {
        if (_grants.Any(grant => grant.Permission == permission))
        {
            return;
        }

        _grants.Add(new RolePermissionGrant(Id, permission));
    }

    /// <summary>
    /// Removes <paramref name="permission"/> and returns the dropped row, or <c>null</c> if it was
    /// not granted.
    /// </summary>
    /// <remarks>
    /// Hands the row back rather than swallowing it because the grant relationship is
    /// <c>Restrict</c>, like every other one in this model — nothing deletes the orphan for us, so
    /// the caller has to pass it to the repository. Returning it is what makes forgetting hard.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown for the owner role. Letting the owner revoke <see cref="PermissionKind.ManageUsers"/>
    /// from their own role is a one-click way to lock every person out of user administration, with
    /// no path back that does not involve editing the database by hand.
    /// </exception>
    public RolePermissionGrant? Revoke(PermissionKind permission)
    {
        if (IsOwnerRole)
        {
            throw new InvalidOperationException(
                $"'{Name}' is the owner role and holds every permission by definition. Revoking "
                + "from it would be a way to lock the last administrator out of user management.");
        }

        var existing = _grants.FirstOrDefault(grant => grant.Permission == permission);

        if (existing is null)
        {
            return null;
        }

        _grants.Remove(existing);

        return existing;
    }

    /// <summary>
    /// Replaces the whole grant set, which is how the role editor saves, and returns the rows that
    /// were dropped so the caller can delete them.
    /// </summary>
    /// <remarks>
    /// The return value is not optional bookkeeping: see <see cref="Revoke"/>. A caller that
    /// ignores it leaves grant rows behind that still say the role allows something.
    /// </remarks>
    public IReadOnlyList<RolePermissionGrant> ReplaceGrants(IEnumerable<PermissionKind> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (IsOwnerRole)
        {
            throw new InvalidOperationException(
                $"'{Name}' is the owner role and holds every permission by definition.");
        }

        var wanted = permissions.Distinct().ToHashSet();
        var dropped = _grants.Where(grant => !wanted.Contains(grant.Permission)).ToList();

        foreach (var grant in dropped)
        {
            _grants.Remove(grant);
        }

        // Only the genuinely new ones are added, so a save that changes nothing leaves the existing
        // rows — and their creation stamps — alone.
        foreach (var permission in wanted.Where(permission => !Allows(permission)))
        {
            _grants.Add(new RolePermissionGrant(Id, permission));
        }

        return dropped;
    }
}
