using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Profit &amp; loss for a period: the gross block above the line (groups that affect gross profit),
/// then indirect income and expense, with both profit figures derived from the same rows.
/// </summary>
public sealed partial class ProfitAndLossViewModel : ObservableObject
{
    private readonly IAccountingReportService _reports;

    [ObservableProperty]
    private DateTime _fromDate = new(DateTime.Today.Year, 4, 1);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<ProfitAndLossRow> _grossRows = [];

    [ObservableProperty]
    private ObservableCollection<ProfitAndLossRow> _indirectRows = [];

    [ObservableProperty]
    private decimal _totalIncome;

    [ObservableProperty]
    private decimal _totalExpense;

    [ObservableProperty]
    private decimal _grossProfit;

    [ObservableProperty]
    private decimal _netProfit;

    [ObservableProperty]
    private string _status = "Press Load for the current financial year.";

    [ObservableProperty]
    private bool _isBusy;

    public ProfitAndLossViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var statement = await _reports.GetProfitAndLossAsync(
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                cancellationToken)
                .ConfigureAwait(true);

            GrossRows = new ObservableCollection<ProfitAndLossRow>(
                statement.Rows.Where(row => row.AffectsGrossProfit));

            IndirectRows = new ObservableCollection<ProfitAndLossRow>(
                statement.Rows.Where(row => !row.AffectsGrossProfit));

            TotalIncome = statement.TotalIncome;
            TotalExpense = statement.TotalExpense;
            GrossProfit = statement.GrossProfit;
            NetProfit = statement.NetProfit;

            Status = $"Period {statement.FromDate:dd-MMM-yyyy} to {statement.ToDate:dd-MMM-yyyy}.";
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
