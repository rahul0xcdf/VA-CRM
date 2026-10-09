using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Security;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Administration group: the Phase 9 user and role masters (phase-9-multiuser.md §4a D2/D4).
/// </summary>
/// <remarks>
/// Ordered last. It is setup rather than daily work — visited when somebody joins or leaves, and
/// left alone in between.
/// <para>
/// Both screens are reachable from here and from the Alt+G palette whoever is signed in, which is
/// the point: the services refuse an Operator rather than the Gateway hiding the entry. A hidden
/// entry is not a permission (§2).
/// </para>
/// </remarks>
public sealed class AdministrationScreenModule : IScreenModule
{
    public string Name => "Administration";

    public int Order => 32;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Users",
            services => new UsersViewModel(
                services.GetRequiredService<IUserAdminService>()),
            typeof(UsersView)),

        new ScreenDescriptor(
            "Roles",
            services => new RolesViewModel(
                services.GetRequiredService<IUserAdminService>()),
            typeof(RolesView)),
    ];
}
