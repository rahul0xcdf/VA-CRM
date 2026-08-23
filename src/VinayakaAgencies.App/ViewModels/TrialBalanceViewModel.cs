using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The trial balance as on a date, with drill-down: selecting a ledger loads its statement for the
/// financial year up to that date (spec §2, §8.4).
/// </summary>
public sealed partial class TrialBalanceViewModel : ObservableObject
{
    private readonly IAccountingReportService _reports;

    [ObservableProperty]
    private DateTime _asOnDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<TrialBalanceRow> _rows = [];

    [ObservableProperty]
    private TrialBalanceRow? _selectedRow;

    [ObservableProperty]
    private ObservableCollection<LedgerStatementRow> _drillDownRows = [];

    [ObservableProperty]
    private decimal _totalDebit;

    [ObservableProperty]
    private decimal _totalCredit;

    [ObservableProperty]
    private string _status = "Press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public TrialBalanceViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    partial void OnSelectedRowChanged(TrialBalanceRow? value)
    {
        if (value is not null)
        {
            LoadDrillDownCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var trialBalance = await _reports.GetTrialBalanceAsync(
                DateOnly.FromDateTime(AsOnDate),
                cancellationToken)
                .ConfigureAwait(true);

            Rows = new ObservableCollection<TrialBalanceRow>(trialBalance.Rows);
            TotalDebit = trialBalance.TotalDebit;
            TotalCredit = trialBalance.TotalCredit;

            Status = trialBalance.IsBalanced
                ? $"Balances to zero exactly ({trialBalance.TotalDebit:N2} each side)."
                : "OUT OF BALANCE — this is a defect, not a rounding artefact.";
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
    private async Task LoadDrillDownAsync(CancellationToken cancellationToken)
    {
        if (SelectedRow is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            // The financial-year start comes from the statement itself being derived from posting:
            // opening balances before it are carried, so showing the full year keeps the two views
            // reconcilable by eye.
            var yearStart = new DateOnly(DateOnly.FromDateTime(AsOnDate).Year, 4, 1);

            var rows = await _reports.GetLedgerStatementAsync(
                SelectedRow.LedgerId,
                yearStart,
                DateOnly.FromDateTime(AsOnDate),
                cancellationToken)
                .ConfigureAwait(true);

            DrillDownRows = new ObservableCollection<LedgerStatementRow>(rows);

            Status = $"{SelectedRow.LedgerName}: {DrillDownRows.Count} line(s) since {yearStart:dd-MMM-yyyy}.";
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
