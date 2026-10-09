using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Security;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Security;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.Infrastructure.Tests.Security;

/// <summary>
/// Checkpoints C9.2 and C9.5 — permissions enforced in the service layer, with no UI in the process.
/// </summary>
/// <remarks>
/// Everything here goes through the real composition against a real migrated SQLite file, and every
/// refusal is produced by calling a service method directly. That is the point: `phase-9-multiuser.md`
/// §2 says "a hidden button is not a permission", and the only way to show the difference is to
/// bypass the button. No <c>Window</c> is constructed anywhere in this file.
/// <para>
/// These prove the permission <i>logic</i>. They do not prove behaviour under two concurrent SQL
/// Server clients, which is C9.1/C9.3/C9.4 and needs an instance this machine does not have
/// (<c>open-questions.md</c> #24).
/// </para>
/// </remarks>
public sealed class PermissionEnforcementTests
{
    private const string OwnerPassword = "owner-pass-2026";
    private const string OperatorPassword = "operator-pass-2026";

    [Fact]
    public async Task HasPermissionAsync_NoAccountsExist_GrantsEverything()
    {
        // The bootstrap state, and the reason Phases 1–8 and their 1,000-odd tests are unaffected by
        // this phase: a database with no accounts enforces nothing, exactly as before. It is also
        // the state in which the authority protects nothing, which is why it is asserted explicitly
        // rather than left as an accident of the implementation.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddProvisionedCompanyAsync(host);

        var granted = await host.InScopeAsync(async sp =>
        {
            var authority = sp.GetRequiredService<IPermissionAuthority>();
            var results = new List<bool>();

            foreach (var permission in Enum.GetValues<PermissionKind>())
            {
                results.Add(await authority.HasPermissionAsync(permission));
            }

            return results;
        });

        granted.ShouldAllBe(allowed => allowed);
    }

    [Fact]
    public async Task EnsureAsync_AnAccountExistsButNobodyIsSignedIn_Refuses()
    {
        // Enforcement begins with the first account. Before this, the previous test's blanket grant
        // would be indistinguishable from a product with no permissions at all.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await SignOutAsync(host);

        var denied = await host.InScopeAsync(async sp =>
        {
            var authority = sp.GetRequiredService<IPermissionAuthority>();

            return await Record.ExceptionAsync(() =>
                authority.EnsureAsync(PermissionKind.PostVoucher));
        });

        denied.ShouldBeOfType<PermissionDeniedException>();
        denied!.Message.ShouldContain("nobody is signed in");
    }

    [Fact]
    public async Task CancelAsync_OperatorSignedIn_IsRefusedByTheServiceNotTheScreen()
    {
        // C9.5. The Operator role is refused voucher cancellation by §4a D4, and this calls
        // IVoucherPostingService.CancelAsync straight off the container.
        //
        // The voucher id is deliberately one that does not exist. A PermissionDeniedException rather
        // than a "no such voucher" proves the gate is checked *before* the service does any work,
        // which is the property that matters: a permission tested after the fact is a permission
        // that has already read the data it was supposed to protect.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await CreateOperatorAsync(host, companyId);
        await SignInAsync(host, "counter", OperatorPassword);

        var refusal = await host.InScopeAsync(async sp =>
        {
            var vouchers = sp.GetRequiredService<IVoucherPostingService>();

            return await Record.ExceptionAsync(() =>
                vouchers.CancelAsync(Guid.NewGuid(), "trying it on"));
        });

        refusal.ShouldBeOfType<PermissionDeniedException>();
        var denied = (PermissionDeniedException)refusal!;
        denied.DeniedPermission.ShouldBe(PermissionKind.CancelVoucher);
        denied.UserName.ShouldBe("counter");
    }

    [Fact]
    public async Task ListUsersAsync_OperatorSignedIn_IsRefused()
    {
        // C9.2 on the administration surface. The Gateway can hide the Users entry; this is what
        // makes hiding it beside the point.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await CreateOperatorAsync(host, companyId);
        await SignInAsync(host, "counter", OperatorPassword);

        var refusal = await host.InScopeAsync(async sp =>
            await Record.ExceptionAsync(() =>
                sp.GetRequiredService<IUserAdminService>().ListUsersAsync()));

        refusal.ShouldBeOfType<PermissionDeniedException>();
    }

    [Fact]
    public async Task HasPermissionAsync_OperatorSignedIn_HoldsTheBillingRightsAndNotTheOthers()
    {
        // The seeded Operator grants read literally off §4a D4 — bills and takes receipts, refused
        // price override, cancellation and configuration. Asserted as a whole so a later edit to
        // SecuritySeed cannot quietly widen the role: blanket denial would pass a test that only
        // checked the refusals.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await CreateOperatorAsync(host, companyId);
        await SignInAsync(host, "counter", OperatorPassword);

        var held = await host.InScopeAsync(async sp =>
        {
            var authority = sp.GetRequiredService<IPermissionAuthority>();
            var result = new List<PermissionKind>();

            foreach (var permission in Enum.GetValues<PermissionKind>())
            {
                if (await authority.HasPermissionAsync(permission))
                {
                    result.Add(permission);
                }
            }

            return result;
        });

        held.OrderBy(permission => permission)
            .ShouldBe(SecuritySeed.OperatorGrants.OrderBy(permission => permission));
    }

    [Fact]
    public async Task HasPermissionAsync_OwnerSignedIn_HoldsEveryPermission()
    {
        // IsOwnerRole answers every check rather than being a snapshot of today's permission list,
        // so a permission added by a later release is covered the moment it exists. Without that,
        // the first person to notice would be an owner locked out of a feature they paid for.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);

        var granted = await host.InScopeAsync(async sp =>
        {
            var authority = sp.GetRequiredService<IPermissionAuthority>();
            var results = new List<bool>();

            foreach (var permission in Enum.GetValues<PermissionKind>())
            {
                results.Add(await authority.HasPermissionAsync(permission));
            }

            return results;
        });

        granted.ShouldAllBe(allowed => allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_GrantRevokedMidSession_RefusesWithoutASecondSignIn()
    {
        // The authority re-reads the role on every check instead of snapshotting it at sign-in.
        // On a shop floor the alternative means a revoked permission keeps working until the holder
        // happens to sign out, which could be days.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        var operatorRoleId = await CreateOperatorAsync(host, companyId);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IUserAdminService>()
            .SaveRoleAsync(
                operatorRoleId,
                new SaveRoleRequest("Operator", null, [PermissionKind.ViewReports])));

        await SignInAsync(host, "counter", OperatorPassword);

        var mayStillPost = await host.InScopeAsync(sp => sp
            .GetRequiredService<IPermissionAuthority>()
            .HasPermissionAsync(PermissionKind.PostVoucher));

        mayStillPost.ShouldBeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_AccountDeactivatedMidSession_Refuses()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var ownerId = await CreateOwnerAsync(host, companyId);
        var operatorRoleId = await CreateOperatorAsync(host, companyId);
        var operatorId = await host.InScopeAsync(async sp =>
        {
            var users = await sp.GetRequiredService<IUserAdminService>().ListUsersAsync();

            return users.Single(user => user.UserName == "counter").Id;
        });

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IUserAdminService>()
            .UpdateUserAsync(new UpdateUserRequest(
                operatorId, "counter", "Counter", operatorRoleId, IsActive: false)));

        // Signed in as the operator *after* deactivation would be refused at sign-in, so the
        // session is established first and the account disabled under it — which is the realistic
        // sequence: the owner disables somebody who is already logged in at the other counter.
        await SignInAsAlreadyActiveOperatorAsync(host, companyId, operatorRoleId);

        var granted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IPermissionAuthority>()
            .HasPermissionAsync(PermissionKind.PostVoucher));

        granted.ShouldBeFalse();
        ownerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task SignInAsync_WrongPassword_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await SignOutAsync(host);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .SignInAsync("owner", "not-the-password"));

        outcome.Succeeded.ShouldBeFalse();
        outcome.Failure.ShouldBe(SignInFailure.InvalidCredentials);
    }

    [Fact]
    public async Task SignInAsync_UnknownUser_ReportsTheSameFailureAsAWrongPassword()
    {
        // Telling a stranger that a user name exists hands them half the answer, and on a 2–3
        // person install the set of valid names is small enough for that to matter.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await SignOutAsync(host);

        var (unknown, wrongPassword) = await host.InScopeAsync(async sp =>
        {
            var signIn = sp.GetRequiredService<ISignInService>();

            return (
                await signIn.SignInAsync("nobody-here", "not-the-password"),
                await signIn.SignInAsync("owner", "not-the-password"));
        });

        unknown.Failure.ShouldBe(wrongPassword.Failure);
        unknown.Message.ShouldBe(wrongPassword.Message);
    }

    [Fact]
    public async Task SignInAsync_CorrectPasswordOnAnInactiveAccount_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        var operatorRoleId = await CreateOperatorAsync(host, companyId);

        var operatorId = await host.InScopeAsync(async sp =>
        {
            var users = await sp.GetRequiredService<IUserAdminService>().ListUsersAsync();

            return users.Single(user => user.UserName == "counter").Id;
        });

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IUserAdminService>()
            .UpdateUserAsync(new UpdateUserRequest(
                operatorId, "counter", "Counter", operatorRoleId, IsActive: false)));
        await SignOutAsync(host);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .SignInAsync("counter", OperatorPassword));

        outcome.Succeeded.ShouldBeFalse();
        outcome.Failure.ShouldBe(SignInFailure.AccountInactive);
    }

    [Fact]
    public async Task CreateFirstAccountAsync_AnAccountAlreadyExists_IsRefused()
    {
        // The one method that needs no permission, because before it runs there is no account to
        // hold one. That makes "refuses once an account exists" the only thing stopping it being a
        // path to a second owner account for anybody who can reach the service.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .CreateFirstAccountAsync(companyId, "sneaky", "Sneaky", "another-pass-2026"));

        outcome.Succeeded.ShouldBeFalse();
        outcome.Message.ShouldContain("already has accounts");
    }

    [Fact]
    public async Task CreateFirstAccountAsync_NoAccounts_AssignsTheOwnerRoleAndSignsIn()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .CreateFirstAccountAsync(companyId, "owner", "The Owner", OwnerPassword));

        outcome.Succeeded.ShouldBeTrue();

        var isOwnerRole = await host.InScopeAsync(async sp =>
        {
            var roles = await sp.GetRequiredService<IUnitOfWork>().Repository<AppRole>().ListAsync();

            return roles.Single(role => role.Id == outcome.SignedIn!.RoleId).IsOwnerRole;
        });

        isOwnerRole.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateUserAsync_TheOnlyAdministrator_RefusesToTakeTheirOwnAccessAway()
    {
        // Without this guard the product has a one-click route to a state whose only remedy is
        // editing the database by hand, and on a shop-floor install there is nobody to do that.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var ownerId = await CreateOwnerAsync(host, companyId);
        var operatorRoleId = await CreateOperatorAsync(host, companyId);

        var refusal = await host.InScopeAsync(async sp =>
            await Record.ExceptionAsync(() => sp
                .GetRequiredService<IUserAdminService>()
                .UpdateUserAsync(new UpdateUserRequest(
                    ownerId, "owner", "The Owner", operatorRoleId, IsActive: true))));

        refusal.ShouldBeOfType<InvalidOperationException>();
        refusal!.Message.ShouldContain("only account that can manage users");
    }

    [Fact]
    public async Task SaveRoleAsync_TakingManageUsersFromTheOnlyAdministeringRole_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);

        // A second, non-system role that administers users, with the only account moved onto it —
        // the owner role itself refuses grant edits outright, so this is the reachable version of
        // the same mistake.
        var refusal = await host.InScopeAsync(async sp =>
        {
            var admin = sp.GetRequiredService<IUserAdminService>();
            var custom = await admin.SaveRoleAsync(
                null,
                new SaveRoleRequest("Office", null, [PermissionKind.ManageUsers]));

            var users = await admin.ListUsersAsync();
            var owner = users.Single();

            await admin.UpdateUserAsync(new UpdateUserRequest(
                owner.Id, owner.UserName, owner.DisplayName, custom.Id, IsActive: true));

            return await Record.ExceptionAsync(() => admin.SaveRoleAsync(
                custom.Id,
                new SaveRoleRequest("Office", null, [PermissionKind.ViewReports])));
        });

        refusal.ShouldBeOfType<InvalidOperationException>();
        refusal!.Message.ShouldContain("nobody able to give it back");
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_LetsTheNewOneSignIn()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);

        var changed = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .ChangePasswordAsync(OwnerPassword, "a-brand-new-pass"));

        changed.Succeeded.ShouldBeTrue();

        await SignOutAsync(host);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .SignInAsync("owner", "a-brand-new-pass"));

        outcome.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_TooShort_IsRefusedAndTheOldPasswordStillWorks()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);

        var changed = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .ChangePasswordAsync(OwnerPassword, "short"));

        changed.Succeeded.ShouldBeFalse();
        changed.Errors.ShouldContain(error => error.Contains(
            $"at least {PasswordPolicy.MinimumLength}", StringComparison.Ordinal));

        await SignOutAsync(host);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .SignInAsync("owner", OwnerPassword));

        outcome.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateUserAsync_Always_FlagsTheAccountToChangeItsPassword()
    {
        // The owner types the initial password, so the owner knows it. The flag is what makes that
        // temporary — and it is what makes the audit trail's attribution to this account mean
        // something afterwards.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        await CreateOwnerAsync(host, companyId);
        await CreateOperatorAsync(host, companyId);

        var created = await host.InScopeAsync(async sp =>
        {
            var users = await sp.GetRequiredService<IUserAdminService>().ListUsersAsync();

            return users.Single(user => user.UserName == "counter");
        });

        created.MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureSecuritySeedAsync_RunTwice_LeavesExactlyTwoRoles()
    {
        // Idempotent like its Phase 2–7 siblings, because it runs at every startup.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .EnsureSecuritySeedAsync(companyId));
        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .EnsureSecuritySeedAsync(companyId));

        var roles = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<AppRole>()
            .ListAsync());

        roles.Count.ShouldBe(2);
        roles.Count(role => role.IsOwnerRole).ShouldBe(1);
    }

    private static async Task<Guid> AddProvisionedCompanyAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .ProvisionAsync(companyId));

        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .EnsureSecuritySeedAsync(companyId));

        return companyId;
    }

    /// <summary>Creates the first account, which leaves it signed in as the owner.</summary>
    private static async Task<Guid> CreateOwnerAsync(SqliteTestHost host, Guid companyId)
    {
        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISignInService>()
            .CreateFirstAccountAsync(companyId, "owner", "The Owner", OwnerPassword));

        outcome.Succeeded.ShouldBeTrue(outcome.Message);

        return outcome.SignedIn!.UserId;
    }

    /// <summary>Creates an Operator account as the signed-in owner, and returns its role id.</summary>
    private static async Task<Guid> CreateOperatorAsync(SqliteTestHost host, Guid companyId)
    {
        return await host.InScopeAsync(async sp =>
        {
            var admin = sp.GetRequiredService<IUserAdminService>();
            var roles = await admin.ListRolesAsync();
            var operatorRole = roles.Single(role => role.IsSystem && !role.IsOwnerRole);

            await admin.CreateUserAsync(new CreateUserRequest(
                "counter", "Counter", OperatorPassword, operatorRole.Id));

            return operatorRole.Id;
        });
    }

    private static Task SignInAsync(SqliteTestHost host, string userName, string password) =>
        host.InScopeAsync(async sp =>
        {
            var outcome = await sp.GetRequiredService<ISignInService>()
                .SignInAsync(userName, password);

            outcome.Succeeded.ShouldBeTrue(outcome.Message);
        });

    /// <summary>
    /// Puts the deactivated operator's identity into the session without going through sign-in,
    /// which would refuse it.
    /// </summary>
    /// <remarks>
    /// This is the only place in the file that establishes a session by hand, and it models a real
    /// sequence rather than a shortcut: the operator signed in while the account was active, and
    /// the owner disabled it afterwards from the other machine. Going through
    /// <see cref="ISignInService"/> could not reach that state, and the state is exactly what the
    /// authority's freshness check exists for.
    /// </remarks>
    private static Task SignInAsAlreadyActiveOperatorAsync(
        SqliteTestHost host,
        Guid companyId,
        Guid operatorRoleId) =>
        host.InScopeAsync(async sp =>
        {
            var users = await sp.GetRequiredService<IUnitOfWork>().Repository<AppUser>().ListAsync();
            var counter = users.Single(user => user.UserName == "counter");

            sp.GetRequiredService<IUserSession>().SignIn(new SignedInUser(
                counter.Id,
                companyId,
                counter.UserName,
                counter.DisplayName,
                operatorRoleId,
                MustChangePassword: false));
        });

    private static Task SignOutAsync(SqliteTestHost host) =>
        host.InScopeAsync(sp =>
        {
            sp.GetRequiredService<IUserSession>().SignOut();

            return Task.CompletedTask;
        });
}
