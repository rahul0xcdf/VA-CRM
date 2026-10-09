using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Security;

/// <summary>
/// One <see cref="PermissionKind"/> held by one <see cref="AppRole"/>.
/// </summary>
/// <remarks>
/// A row per grant rather than a delimited column on the role, so that a unique index can stop the
/// same permission being granted twice and the user-admin screen can show grants as data instead of
/// parsing a string.
/// <para>
/// Implements <see cref="IBelongsToAggregate"/> so the edit log files a grant change under its role
/// — non-negotiable #3 covers masters, and who may do what is a master worth being able to read the
/// history of. <see cref="RoleId"/> is denormalised onto the row for the same reason
/// <c>JournalLine.VoucherId</c> is: the audit writer needs the root's id without loading it.
/// </para>
/// </remarks>
public sealed class RolePermissionGrant : Entity, IBelongsToAggregate, ICreationStamped
{
    private RolePermissionGrant()
    {
        // EF Core materialisation.
    }

    public RolePermissionGrant(Guid roleId, PermissionKind permission)
    {
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("A grant must belong to a role.", nameof(roleId));
        }

        RoleId = roleId;
        Permission = permission;
    }

    public Guid RoleId { get; private set; }

    public PermissionKind Permission { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(AppRole);

    public string AggregateId => RoleId.ToString();
}
