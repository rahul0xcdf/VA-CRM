using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.Tests.TestSupport;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Security;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Security;
using Shouldly;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>
/// The Phase 9 screens, driven against the application's real composition.
/// </summary>
/// <remarks>
/// Against a real database and the real services rather than stubs, for the reason
/// <see cref="RealDatabaseAppHost"/> gives: a screen proven against fakes proves the fakes agree
/// with each other. These are also where the permission refusals are seen from the operator's
/// side — a refusal the service raises and the screen drops on the floor is a refusal nobody is
/// told about, which on a shop counter reads as the software having silently done nothing.
/// </remarks>
public sealed class Phase9ScreenTests
{
    [Fact]
    public async Task LoginViewModel_NoAccountsExist_OffersToCreateTheFirst()
    {
        // The first launch after this phase. Nothing is seeded, so the window has to offer the
        // owner account rather than a sign-in prompt nobody can satisfy.
        await using var host = await RealDatabaseAppHost.StartAsync();
        var viewModel = NewLogin(host);

        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.IsFirstAccount.ShouldBeTrue();
        viewModel.ShowSignIn.ShouldBeFalse();
        viewModel.Status.ShouldContain("No accounts exist yet");
    }

    [Fact]
    public async Task LoginViewModel_CreateFirstAccount_SignsInAsTheOwner()
    {
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);

        var viewModel = NewLogin(host);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.UserName = "owner";
        viewModel.DisplayName = "The Owner";
        viewModel.NewPassword = "owner-pass-2026";
        viewModel.ConfirmPassword = "owner-pass-2026";

        await viewModel.CreateFirstAccountCommand.ExecuteAsync(null);

        viewModel.IsSignedIn.ShouldBeTrue();
        host.Services.GetRequiredService<IUserSession>().Current!.UserName.ShouldBe("owner");
    }

    [Fact]
    public async Task LoginViewModel_PasswordsDoNotMatch_RefusesAndSaysSo()
    {
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);

        var viewModel = NewLogin(host);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.UserName = "owner";
        viewModel.NewPassword = "owner-pass-2026";
        viewModel.ConfirmPassword = "owner-pass-2027";

        await viewModel.CreateFirstAccountCommand.ExecuteAsync(null);

        viewModel.IsSignedIn.ShouldBeFalse();
        viewModel.Status.ShouldContain("do not match");
    }

    [Fact]
    public async Task LoginViewModel_WrongPassword_ClearsTheFieldAndStaysPut()
    {
        // A failed attempt leaving the password on screen is how the next person at the counter
        // reads it.
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);
        await CreateOwnerAsync(host);
        host.Services.GetRequiredService<IUserSession>().SignOut();

        var viewModel = NewLogin(host);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.UserName = "owner";
        viewModel.Password = "wrong-password";

        await viewModel.SignInCommand.ExecuteAsync(null);

        viewModel.IsSignedIn.ShouldBeFalse();
        viewModel.Password.ShouldBeEmpty();
        viewModel.Status.ShouldContain("do not match an account");
    }

    [Fact]
    public async Task LoginViewModel_AccountWithAPasswordTheOwnerSet_RoutesToChangingIt()
    {
        // Routed rather than refused: an operator who cannot get in also cannot change their
        // password. And this is what makes the owner knowing the initial password temporary.
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);
        await CreateOwnerAsync(host);
        await CreateOperatorAsync(host);
        host.Services.GetRequiredService<IUserSession>().SignOut();

        var viewModel = NewLogin(host);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.UserName = "counter";
        viewModel.Password = "counter-pass-2026";
        await viewModel.SignInCommand.ExecuteAsync(null);

        viewModel.MustChangePassword.ShouldBeTrue();
        viewModel.IsSignedIn.ShouldBeFalse();
        viewModel.ShowSignIn.ShouldBeFalse();

        viewModel.Password = "counter-pass-2026";
        viewModel.NewPassword = "my-own-password";
        viewModel.ConfirmPassword = "my-own-password";
        await viewModel.ChangePasswordCommand.ExecuteAsync(null);

        viewModel.IsSignedIn.ShouldBeTrue();
    }

    [Fact]
    public async Task UsersViewModel_Refresh_LoadsTheSeededRolesAndSaysWhatNoAccountsMeans()
    {
        // The empty state has to say what it actually means. "No accounts" sounds like nothing is
        // configured; what it means is that everybody on this install can do everything.
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);

        var viewModel = new UsersViewModel(host.Services.GetRequiredService<IUserAdminService>());

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.RoleChoices.Count.ShouldBe(2);
        viewModel.Users.ShouldBeEmpty();
        viewModel.Status.ShouldContain("can do everything");
    }

    [Fact]
    public async Task UsersViewModel_Save_CreatesAnAccountThatMustChangeItsPassword()
    {
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);
        await CreateOwnerAsync(host);

        var viewModel = new UsersViewModel(host.Services.GetRequiredService<IUserAdminService>());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.EditUserName = "counter";
        viewModel.EditDisplayName = "Counter";
        viewModel.EditPassword = "counter-pass-2026";
        viewModel.EditRole = viewModel.RoleChoices.Single(role => !role.IsOwnerRole);

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.Users.ShouldContain(user => user.UserName == "counter" && user.MustChangePassword);
        viewModel.EditPassword.ShouldBeEmpty();
    }

    [Fact]
    public async Task UsersViewModel_OperatorSignedIn_ShowsTheRefusalRatherThanThrowing()
    {
        // C9.2 from the screen's side. An Operator can reach Users through the Alt+G palette, so
        // the service refusing is the protection — and the screen has to say so rather than
        // appearing to load nothing.
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);
        await CreateOwnerAsync(host);
        await CreateOperatorAsync(host);
        await SignInAsOperatorAsync(host);

        var viewModel = new UsersViewModel(host.Services.GetRequiredService<IUserAdminService>());

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Users.ShouldBeEmpty();
        viewModel.Status.ShouldContain(PermissionKind.ManageUsers.ToString());
    }

    [Fact]
    public async Task RolesViewModel_SelectingTheOwnerRole_LocksItsGrants()
    {
        // It holds everything by definition, including permissions a later release adds. Letting
        // the grants be edited would be a way for an owner to lock themselves out of a feature.
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);

        var viewModel = new RolesViewModel(host.Services.GetRequiredService<IUserAdminService>());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SelectedRole = viewModel.Roles.Single(role => role.IsOwnerRole);
        await viewModel.SelectionChangedCommand.ExecuteAsync(null);

        viewModel.IsOwnerRoleSelected.ShouldBeTrue();
        viewModel.Status.ShouldContain("cannot be edited");
    }

    [Fact]
    public async Task RolesViewModel_Always_OffersEveryPermissionTheProductHas()
    {
        // Built from the enum rather than a curated list. A right that exists, is enforced, and
        // cannot be granted from the one screen for granting it is the failure this prevents.
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);

        var viewModel = new RolesViewModel(host.Services.GetRequiredService<IUserAdminService>());

        viewModel.Permissions
            .Select(choice => choice.Permission)
            .OrderBy(permission => permission)
            .ShouldBe(Enum.GetValues<PermissionKind>().OrderBy(permission => permission));
    }

    [Fact]
    public async Task RolesViewModel_Save_RoundTripsTheTickedPermissions()
    {
        await using var host = await RealDatabaseAppHost.StartAsync();
        await SeedRolesAsync(host);
        await CreateOwnerAsync(host);

        var viewModel = new RolesViewModel(host.Services.GetRequiredService<IUserAdminService>());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.NewCommand.Execute(null);
        viewModel.EditName = "Accountant";

        foreach (var choice in viewModel.Permissions)
        {
            choice.IsGranted = choice.Permission
                is PermissionKind.ViewReports or PermissionKind.RunStatutoryReturns;
        }

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = viewModel.Roles.Single(role => role.Name == "Accountant");
        saved.Grants.OrderBy(permission => permission).ShouldBe(
            new[] { PermissionKind.ViewReports, PermissionKind.RunStatutoryReturns }
                .OrderBy(permission => permission));
    }

    [Fact]
    public async Task FirstRunCompanyViewModel_Create_LeavesTheInstallSecured()
    {
        // The bootstrap state — no accounts, so every permission granted to everybody — is now
        // unreachable from a fresh install, because the owner account is created in the same step
        // as the company rather than left to the next window.
        await using var host = await RealDatabaseAppHost.StartAsync(provisionCompany: false);
        var viewModel = NewFirstRun(host);

        viewModel.TradingName = "Vinayaka Agencies";
        viewModel.LegalName = "Vinayaka Agencies (Regd.)";
        viewModel.SelectedState = viewModel.States.First(state => state.Code == "29");
        viewModel.OwnerUserName = "owner";
        viewModel.OwnerDisplayName = "The Owner";
        viewModel.OwnerPassword = "owner-pass-2026";
        viewModel.OwnerPasswordAgain = "owner-pass-2026";

        await viewModel.CreateCommand.ExecuteAsync(null);

        viewModel.CreatedCompanyId.ShouldNotBeNull(viewModel.Status);

        // Signed in as it created them, so the startup path does not ask again.
        var session = host.Services.GetRequiredService<IUserSession>();
        session.IsSignedIn.ShouldBeTrue();
        session.Current!.UserName.ShouldBe("owner");

        // And the install is no longer in the state where everything is permitted.
        var accountsExist = await host.Services.GetRequiredService<ISignInService>()
            .AnyAccountsExistAsync();

        accountsExist.ShouldBeTrue();
    }

    [Fact]
    public async Task FirstRunCompanyViewModel_Create_AssignsTheOwnerRole()
    {
        await using var host = await RealDatabaseAppHost.StartAsync(provisionCompany: false);
        var viewModel = NewFirstRun(host);

        viewModel.TradingName = "Vinayaka Agencies";
        viewModel.LegalName = "Vinayaka Agencies (Regd.)";
        viewModel.SelectedState = viewModel.States.First(state => state.Code == "29");
        viewModel.OwnerUserName = "owner";
        viewModel.OwnerPassword = "owner-pass-2026";
        viewModel.OwnerPasswordAgain = "owner-pass-2026";

        await viewModel.CreateCommand.ExecuteAsync(null);

        viewModel.CreatedCompanyId.ShouldNotBeNull(viewModel.Status);

        var roles = await host.Services.GetRequiredService<IUserAdminService>().ListRolesAsync();
        var assigned = host.Services.GetRequiredService<IUserSession>().Current!.RoleId;

        roles.Single(role => role.Id == assigned).IsOwnerRole.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "owner-pass-2026", "owner-pass-2026", "Choose the user name")]
    [InlineData("owner", "short", "short", "at least")]
    [InlineData("owner", "owner-pass-2026", "owner-pass-2027", "do not match")]
    public async Task FirstRunCompanyViewModel_IncompleteAccount_CreatesNothingAtAll(
        string userName,
        string password,
        string again,
        string expected)
    {
        // Validated before the company is created, not after. A company that exists with no account
        // is exactly the state this change is removing, so a bad password must not produce one.
        await using var host = await RealDatabaseAppHost.StartAsync(provisionCompany: false);
        var viewModel = NewFirstRun(host);

        viewModel.TradingName = "Vinayaka Agencies";
        viewModel.LegalName = "Vinayaka Agencies (Regd.)";
        viewModel.SelectedState = viewModel.States.First(state => state.Code == "29");
        viewModel.OwnerUserName = userName;
        viewModel.OwnerPassword = password;
        viewModel.OwnerPasswordAgain = again;

        await viewModel.CreateCommand.ExecuteAsync(null);

        viewModel.CreatedCompanyId.ShouldBeNull();
        viewModel.Status.ShouldContain(expected);

        var companies = await host.Services.GetRequiredService<ICompanyService>().CountAsync();
        companies.ShouldBe(0);
    }

    private static FirstRunCompanyViewModel NewFirstRun(RealDatabaseAppHost host) =>
        new(
            host.Services.GetRequiredService<ICompanyService>(),
            host.Services.GetRequiredService<ICompanyProvisioningService>(),
            host.Services.GetRequiredService<ISignInService>());

    private static LoginViewModel NewLogin(RealDatabaseAppHost host) =>
        new(host.Services.GetRequiredService<ISignInService>(), host.CompanyId);

    private static Task SeedRolesAsync(RealDatabaseAppHost host) =>
        host.Services.GetRequiredService<ICompanyProvisioningService>()
            .EnsureSecuritySeedAsync(host.CompanyId);

    private static async Task CreateOwnerAsync(RealDatabaseAppHost host)
    {
        var outcome = await host.Services.GetRequiredService<ISignInService>()
            .CreateFirstAccountAsync(host.CompanyId, "owner", "The Owner", "owner-pass-2026");

        outcome.Succeeded.ShouldBeTrue(outcome.Message);
    }

    private static async Task CreateOperatorAsync(RealDatabaseAppHost host)
    {
        var admin = host.Services.GetRequiredService<IUserAdminService>();
        var roles = await admin.ListRolesAsync();
        var operatorRole = roles.Single(role => role.IsSystem && !role.IsOwnerRole);

        await admin.CreateUserAsync(new CreateUserRequest(
            "counter", "Counter", "counter-pass-2026", operatorRole.Id));
    }

    private static async Task SignInAsOperatorAsync(RealDatabaseAppHost host)
    {
        var signIn = host.Services.GetRequiredService<ISignInService>();

        signIn.SignOut();

        var outcome = await signIn.SignInAsync("counter", "counter-pass-2026");

        outcome.Succeeded.ShouldBeTrue(outcome.Message);
    }
}
