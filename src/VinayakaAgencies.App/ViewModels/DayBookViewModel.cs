using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The day book: every current revision in a date range, cancelled vouchers shown and flagged
/// (spec §8.4) — hiding them would make the numbering gap rule look like a bug.
/// </summary>
public sealed partial class DayBookViewModel : ObservableObject
{
    private readonly IAccountingReportService _reports;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<DayBookRow> _rows = [];

    [ObservableProperty]
    private decimal _totalDebit;

    [ObservableProperty]
    private decimal _totalCredit;

    [ObservableProperty]
    private string _status = "Choose a range and press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public DayBookViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var rows = await _reports.GetDayBookAsync(
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                cancellationToken)
                .ConfigureAwait(true);

            Rows = new ObservableCollection<DayBookRow>(rows);
            TotalDebit = rows.Sum(row => row.Debit);
            TotalCredit = rows.Sum(row => row.Credit);

            Status = $"{rows.Count} line(s); cancelled vouchers are flagged.";
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
