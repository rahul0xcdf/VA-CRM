using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// The two roles `phase-9-multiuser.md` §4a D4 settles on, created idempotently.
/// </summary>
/// <remarks>
/// <b>Roles are seeded; users are not.</b> A seeded account would ship every install with a known
/// user name and a password somebody has to guess is the default — which is how products end up
/// with an <c>admin</c>/<c>admin</c> that nobody changed. The first account is created by whoever
/// is sitting in front of the first launch, through
/// <see cref="ISignInService.CreateFirstAccountAsync"/>.
/// <para>
/// Called from one place by two callers — the seed step and first-account creation — so the grants
/// the Operator role starts with cannot differ depending on which path created it.
/// </para>
/// </remarks>
public static class SecuritySeed
{
    /// <summary>Name of the all-permissions role.</summary>
    public const string OwnerRoleName = "Owner";

    /// <summary>Name of the billing role.</summary>
    public const string OperatorRoleName = "Operator";

    /// <summary>
    /// What an Operator starts with.
    /// </summary>
    /// <remarks>
    /// Read literally off D4 — "bills and takes receipts, and is refused price override, voucher
    /// cancellation and configuration". Masters are included because an operator who cannot create
    /// a customer cannot bill a new one. Statutory returns, payroll and data migration are excluded
    /// as owner work rather than billing.
    /// <para>
    /// These are a starting point, not a ruling: roles are data (D4), so the owner changes them on
    /// the Roles screen without a release. Worth confirming at walkthrough.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<PermissionKind> OperatorGrants =
    [
        PermissionKind.ManageMasters,
        PermissionKind.PostVoucher,
        PermissionKind.ViewReports,
    ];

    /// <summary>
    /// Ensures both roles exist for <paramref name="companyId"/> and returns the owner role.
    /// </summary>
    /// <remarks>
    /// Stages its writes on <paramref name="unitOfWork"/> without saving, so a caller can create
    /// the roles and the first account in one transaction. Matching an existing role by its
    /// <see cref="AppRole.IsOwnerRole"/> flag rather than by name, because the owner may have
    /// renamed it.
    /// </remarks>
    public static async Task<AppRole> EnsureRolesAsync(
        IUnitOfWork unitOfWork,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        var roles = unitOfWork.Repository<AppRole>();
        var existing = await roles
            .ListAsync(new RolesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var owner = existing.FirstOrDefault(role => role.IsOwnerRole);

        if (owner is null)
        {
            owner = new AppRole(companyId, OwnerRoleName, isSystem: true, isOwnerRole: true);
            owner.SetDescription("Full access, including price override, voucher cancellation and configuration.");

            // Populated even though IsOwnerRole already answers every check, so the Roles screen
            // can show what the role covers rather than an empty list.
            foreach (var permission in Enum.GetValues<PermissionKind>())
            {
                owner.Grant(permission);
            }

            await roles.AddAsync(owner, cancellationToken).ConfigureAwait(false);
        }

        if (!existing.Any(role => role.IsSystem && !role.IsOwnerRole))
        {
            var operatorRole = new AppRole(companyId, OperatorRoleName, isSystem: true);
            operatorRole.SetDescription("Billing and receipts. No price override, no voucher cancellation, no configuration.");

            foreach (var permission in OperatorGrants)
            {
                operatorRole.Grant(permission);
            }

            await roles.AddAsync(operatorRole, cancellationToken).ConfigureAwait(false);
        }

        return owner;
    }

    private sealed class RolesOfCompany : Specification<AppRole>
    {
        public RolesOfCompany(Guid companyId)
        {
            Where(role => role.CompanyId == companyId);
            AddInclude(role => role.Grants);
        }
    }
}
