using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Sales settings: the price-override gate and the credit-limit policy
/// (phase-4-customer-pricing.md §2, §4a). Until Phase 9 brings user roles, this row IS the
/// permission store for the pricing override.
/// </summary>
public sealed partial class SalesSettingsViewModel : ObservableObject
{
    private readonly ISalesSettingsService _settings;

    [ObservableProperty]
    private bool _allowPriceOverride = true;

    [ObservableProperty]
    private CreditLimitPolicy _creditLimitPolicy = CreditLimitPolicy.Warn;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public SalesSettingsViewModel(ISalesSettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<CreditLimitPolicy> PolicyChoices { get; } =
        [CreditLimitPolicy.Warn, CreditLimitPolicy.Block];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var current = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            AllowPriceOverride = current.AllowPriceOverride;
            CreditLimitPolicy = current.CreditLimitPolicy;
            Status = "Loaded.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var saved = await _settings.UpdateAsync(
                    new SaveSalesSettingsRequest(AllowPriceOverride, CreditLimitPolicy),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = saved.AllowPriceOverride
                ? "Saved. Operators may override auto-applied prices; every override is logged."
                : "Saved. Auto-applied prices are locked on every sale screen.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
