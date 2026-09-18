using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The day book: every current revision in a date range, cancelled vouchers shown and flagged
/// (spec §8.4) — hiding them would make the numbering gap rule look like a bug.
/// </summary>
/// <remarks>
/// <c>Ctrl+Enter</c> gathers the highlighted row's whole voucher (ADR-0007). The day book lists
/// voucher <em>lines</em>, so a payment appears as two rows pages apart once the range is sorted by
/// date; seeing the voucher entire is what turns a line into something an operator can check.
/// </remarks>
public sealed partial class DayBookViewModel : ObservableObject, IScreenShortcuts
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
    private DayBookRow? _selectedRow;

    [ObservableProperty]
    private ObservableCollection<DayBookRow> _voucherRows = [];

    [ObservableProperty]
    private string _status = "Choose a range and press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public DayBookViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Whole voucher", ShowVoucherCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Ctrl+Enter The whole voucher behind the highlighted line · Esc Back";

    /// <summary>
    /// Ctrl+Enter: every line of the highlighted row's voucher.
    /// </summary>
    /// <remarks>
    /// Filters the rows already loaded rather than asking the database again. The day book query
    /// returns every line of every voucher in the range, so the voucher is already in hand — a second
    /// round trip would fetch data this screen is holding.
    /// </remarks>
    [RelayCommand]
    private void ShowVoucher()
    {
        if (SelectedRow is null)
        {
            Status = "Highlight a line first, then press Ctrl+Enter.";
            return;
        }

        var voucherId = SelectedRow.VoucherId;

        VoucherRows = new ObservableCollection<DayBookRow>(
            Rows.Where(row => row.VoucherId == voucherId));

        Status = $"{SelectedRow.VoucherNumber}: {VoucherRows.Count} line(s)" +
                 (SelectedRow.IsCancelled ? " — this voucher is cancelled." : ".");
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
