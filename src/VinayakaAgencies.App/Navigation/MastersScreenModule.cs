using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Masters group: chart-of-accounts screens (spec §2).
/// </summary>
public sealed class MastersScreenModule : IScreenModule
{
    public string Name => "Masters";

    public int Order => 10;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Account groups",
            services => new AccountGroupsViewModel(
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(AccountGroupsView)),

        new ScreenDescriptor(
            "Ledgers",
            services => new LedgersViewModel(
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(LedgersView)),
    ];
}
