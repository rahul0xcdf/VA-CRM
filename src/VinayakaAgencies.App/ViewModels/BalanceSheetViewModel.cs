using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The balance sheet as of a date: assets against liabilities, with the derived net profit carried
/// into the Profit &amp; Loss A/c line (spec §8.4).
/// </summary>
public sealed partial class BalanceSheetViewModel : ObservableObject
{
    private readonly IAccountingReportService _reports;

    [ObservableProperty]
    private DateTime _asOnDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<BalanceSheetRow> _assets = [];

    [ObservableProperty]
    private ObservableCollection<BalanceSheetRow> _liabilities = [];

    [ObservableProperty]
    private decimal _totalAssets;

    [ObservableProperty]
    private decimal _totalLiabilities;

    [ObservableProperty]
    private string _status = "Press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public BalanceSheetViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var sheet = await _reports.GetBalanceSheetAsync(
                DateOnly.FromDateTime(AsOnDate),
                cancellationToken)
                .ConfigureAwait(true);

            Assets = new ObservableCollection<BalanceSheetRow>(sheet.Assets);
            Liabilities = new ObservableCollection<BalanceSheetRow>(sheet.Liabilities);
            TotalAssets = sheet.TotalAssets;
            TotalLiabilities = sheet.TotalLiabilities;

            Status = sheet.IsBalanced
                ? $"Balances exactly as on {sheet.AsOnDate:dd-MMM-yyyy}."
                : "OUT OF BALANCE — a non-zero Suspense A/c is often the visible cause.";
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
