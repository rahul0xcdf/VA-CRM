using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Creates and alters accounts and roles, behind <see cref="PermissionKind.ManageUsers"/>.
/// </summary>
/// <remarks>
/// <b>The lockout guard.</b> Every path that could remove the last person able to administer users
/// — deactivating them, moving them to a role without the right, or editing that role's grants — is
/// refused. Without it the product has a one-click way to reach a state whose only remedy is editing
/// the database by hand, and on a shop-floor install there is nobody to do that. This is why
/// <see cref="AppRole.Revoke"/> also refuses the owner role outright.
/// </remarks>
public sealed class UserAdminService : IUserAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionAuthority _permissions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICompanyContext _companyContext;

    public UserAdminService(
        IUnitOfWork unitOfWork,
        IPermissionAuthority permissions,
        IPasswordHasher passwordHasher,
        ICompanyContext companyContext)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
    }

    public async Task<IReadOnlyList<AppUserSummary>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var users = await _unitOfWork.Repository<AppUser>()
            .ListAsync(new UsersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);
        var roles = await _unitOfWork.Repository<AppRole>()
            .ListAsync(new RolesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var roleNames = roles.ToDictionary(role => role.Id, role => role.Name);

        return [.. users.Select(user => ToSummary(user, roleNames))];
    }

    public async Task<IReadOnlyList<AppRoleSummary>> ListRolesAsync(
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var roles = await _unitOfWork.Repository<AppRole>()
            .ListAsync(new RolesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        return [.. roles.Select(ToSummary)];
    }

    public async Task<AppUserSummary> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        var passwordErrors = PasswordPolicy.Validate(request.Password);

        if (passwordErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", passwordErrors));
        }

        await EnsureUserNameFreeAsync(request.UserName, null, cancellationToken).ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var role = await RequireRoleAsync(request.RoleId, companyId, cancellationToken)
            .ConfigureAwait(false);

        var user = new AppUser(
            companyId,
            request.UserName,
            request.DisplayName,
            _passwordHasher.Hash(request.Password),
            role.Id,
            mustChangePassword: true);

        await _unitOfWork.Repository<AppUser>().AddAsync(user, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(user, new Dictionary<Guid, string> { [role.Id] = role.Name });
    }

    public async Task<AppUserSummary> UpdateUserAsync(
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        var user = await _unitOfWork.Repository<AppUser>()
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("That account no longer exists.");

        await EnsureUserNameFreeAsync(request.UserName, user.Id, cancellationToken).ConfigureAwait(false);

        var role = await RequireRoleAsync(request.RoleId, user.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        // Work out what the account would look like after the change, then check the company would
        // still have an administrator. Checking before applying is what makes the guard correct for
        // a deactivation and a role move in the same save.
        var wouldAdminister = request.IsActive && role.Allows(PermissionKind.ManageUsers);

        if (!wouldAdminister)
        {
            await EnsureAnotherAdministratorRemainsAsync(user, cancellationToken).ConfigureAwait(false);
        }

        user.SetUserName(request.UserName);
        user.SetDisplayName(request.DisplayName);
        user.AssignRole(role.Id);

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        _unitOfWork.Repository<AppUser>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(user, new Dictionary<Guid, string> { [role.Id] = role.Name });
    }

    public async Task ResetPasswordAsync(
        Guid userId,
        string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        var errors = PasswordPolicy.Validate(temporaryPassword);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var user = await _unitOfWork.Repository<AppUser>()
            .GetByIdAsync(userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("That account no longer exists.");

        user.ResetPassword(_passwordHasher.Hash(temporaryPassword));
        _unitOfWork.Repository<AppUser>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppRoleSummary> SaveRoleAsync(
        Guid? roleId,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var roles = _unitOfWork.Repository<AppRole>();
        var existing = await roles
            .ListAsync(new RolesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var normalised = NameNormaliser.Normalise(request.Name);
        var clash = existing.FirstOrDefault(role =>
            role.NameNormalised == normalised && role.Id != roleId);

        if (clash is not null)
        {
            throw new InvalidOperationException($"A role called '{clash.Name}' already exists.");
        }

        AppRole role;

        if (roleId is null)
        {
            role = new AppRole(companyId, request.Name);
            role.SetDescription(request.Description);
            // A new role has nothing to drop, so the return is empty by construction.
            _ = role.ReplaceGrants(request.Grants);

            await roles.AddAsync(role, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            role = existing.FirstOrDefault(candidate => candidate.Id == roleId.Value)
                ?? throw new InvalidOperationException("That role no longer exists.");

            role.SetName(request.Name);
            role.SetDescription(request.Description);

            // The owner role holds everything by definition and refuses grant edits; renaming it is
            // still allowed, so this is skipped rather than refused.
            if (!role.IsOwnerRole)
            {
                var losesAdministration = !request.Grants.Contains(PermissionKind.ManageUsers)
                    && role.Allows(PermissionKind.ManageUsers);

                if (losesAdministration)
                {
                    await EnsureAdministratorsOutsideRoleAsync(role, cancellationToken)
                        .ConfigureAwait(false);
                }

                // Restrict on the relationship means nothing deletes these for us.
                var dropped = role.ReplaceGrants(request.Grants);

                foreach (var grant in dropped)
                {
                    _unitOfWork.Repository<RolePermissionGrant>().Remove(grant);
                }
            }

            roles.Update(role);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(role);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await _permissions.EnsureAsync(PermissionKind.ManageUsers, cancellationToken).ConfigureAwait(false);

        // With grants included: the relationship is Restrict, so they have to be removed here or the
        // delete fails on a foreign key the operator cannot be expected to interpret.
        var role = await _unitOfWork.Repository<AppRole>()
            .SingleOrDefaultAsync(new RoleWithGrants(roleId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("That role no longer exists.");

        if (role.IsSystem)
        {
            throw new InvalidOperationException(
                $"'{role.Name}' is one of the roles the product seeds and cannot be deleted. "
                + "Change its grants instead, or move its users to another role.");
        }

        var assigned = await _unitOfWork.Repository<AppUser>()
            .CountAsync(new UsersInRole(roleId), cancellationToken)
            .ConfigureAwait(false);

        if (assigned > 0)
        {
            throw new InvalidOperationException(
                $"'{role.Name}' still has {assigned} account(s) assigned. Move them to another role "
                + "first — an account whose role has gone holds no permissions and cannot be told why.");
        }

        foreach (var grant in role.Grants.ToList())
        {
            _unitOfWork.Repository<RolePermissionGrant>().Remove(grant);
        }

        _unitOfWork.Repository<AppRole>().Remove(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureUserNameFreeAsync(
        string userName,
        Guid? exceptUserId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var normalised = NameNormaliser.Normalise(userName);
        var clash = await _unitOfWork.Repository<AppUser>()
            .SingleOrDefaultAsync(new UserByName(normalised), cancellationToken)
            .ConfigureAwait(false);

        if (clash is not null && clash.Id != exceptUserId)
        {
            throw new InvalidOperationException($"An account called '{clash.UserName}' already exists.");
        }
    }

    private async Task<AppRole> RequireRoleAsync(
        Guid roleId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Repository<AppRole>()
            .SingleOrDefaultAsync(new RoleWithGrants(roleId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("That role no longer exists.");

        if (role.CompanyId != companyId)
        {
            throw new InvalidOperationException("That role belongs to a different company.");
        }

        return role;
    }

    /// <summary>
    /// Throws unless some account other than <paramref name="losing"/> can still manage users.
    /// </summary>
    private async Task EnsureAnotherAdministratorRemainsAsync(
        AppUser losing,
        CancellationToken cancellationToken)
    {
        var others = await ActiveAdministratorsAsync(losing.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (others.Any(user => user.Id != losing.Id))
        {
            return;
        }

        throw new InvalidOperationException(
            $"'{losing.DisplayName}' is the only account that can manage users. Give another "
            + "account that permission first — otherwise nobody can undo this.");
    }

    /// <summary>
    /// Throws unless some active account outside <paramref name="role"/> can still manage users.
    /// </summary>
    private async Task EnsureAdministratorsOutsideRoleAsync(
        AppRole role,
        CancellationToken cancellationToken)
    {
        var administrators = await ActiveAdministratorsAsync(role.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (administrators.Any(user => user.RoleId != role.Id))
        {
            return;
        }

        throw new InvalidOperationException(
            $"'{role.Name}' holds the only accounts that can manage users. Taking that permission "
            + "away would leave nobody able to give it back.");
    }

    private async Task<IReadOnlyList<AppUser>> ActiveAdministratorsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var users = await _unitOfWork.Repository<AppUser>()
            .ListAsync(new UsersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);
        var roles = await _unitOfWork.Repository<AppRole>()
            .ListAsync(new RolesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var administering = roles
            .Where(role => role.Allows(PermissionKind.ManageUsers))
            .Select(role => role.Id)
            .ToHashSet();

        return [.. users.Where(user => user.IsActive && administering.Contains(user.RoleId))];
    }

    private static AppUserSummary ToSummary(AppUser user, Dictionary<Guid, string> roleNames) =>
        new(
            user.Id,
            user.CompanyId,
            user.UserName,
            user.DisplayName,
            user.RoleId,
            roleNames.TryGetValue(user.RoleId, out var name) ? name : "(role missing)",
            user.IsActive,
            user.MustChangePassword,
            user.LastSignInAtUtc);

    private static AppRoleSummary ToSummary(AppRole role) =>
        new(
            role.Id,
            role.CompanyId,
            role.Name,
            role.Description,
            role.IsSystem,
            role.IsOwnerRole,
            [.. role.Grants.Select(grant => grant.Permission).OrderBy(permission => permission)]);

    private sealed class UsersOfCompany : Specification<AppUser>
    {
        public UsersOfCompany(Guid companyId)
        {
            Where(user => user.CompanyId == companyId);
            ApplyOrderBy(user => user.UserName);
        }
    }

    private sealed class UsersInRole : Specification<AppUser>
    {
        public UsersInRole(Guid roleId)
        {
            Where(user => user.RoleId == roleId);
        }
    }

    private sealed class UserByName : Specification<AppUser>
    {
        public UserByName(string userNameNormalised)
        {
            Where(user => user.UserNameNormalised == userNameNormalised);
        }
    }

    private sealed class RolesOfCompany : Specification<AppRole>
    {
        public RolesOfCompany(Guid companyId)
        {
            Where(role => role.CompanyId == companyId);
            AddInclude(role => role.Grants);
            ApplyOrderBy(role => role.Name);
        }
    }

    private sealed class RoleWithGrants : Specification<AppRole>
    {
        public RoleWithGrants(Guid roleId)
        {
            Where(role => role.Id == roleId);
            AddInclude(role => role.Grants);
        }
    }
}
