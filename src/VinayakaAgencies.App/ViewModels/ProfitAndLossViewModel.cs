using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Profit &amp; loss for a period: the gross block above the line (groups that affect gross profit),
/// then indirect income and expense, with both profit figures derived from the same rows.
/// </summary>
/// <remarks>
/// <c>Ctrl+Enter</c> on a line opens that ledger's statement for the same period (ADR-0007). A P&amp;L
/// figure an operator cannot question is a figure they have to take on trust, and "which postings
/// make this number" is the only question anybody asks of one.
/// </remarks>
public sealed partial class ProfitAndLossViewModel : ObservableObject, IScreenShortcuts
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
    private ProfitAndLossRow? _selectedGrossRow;

    [ObservableProperty]
    private ProfitAndLossRow? _selectedIndirectRow;

    /// <summary>
    /// The one highlighted row, whichever block it is in.
    /// </summary>
    /// <remarks>
    /// The gross block and the indirect block are one statement split for reading, so exactly one row
    /// should ever be highlighted and Ctrl+Enter should drill that one. Binding both grids straight to
    /// a single property does not work — each grid would reject the other's row as not being in its
    /// own collection and null the property back out — so each keeps its own selection and setting one
    /// clears the other.
    /// </remarks>
    [ObservableProperty]
    private ProfitAndLossRow? _selectedRow;

    partial void OnSelectedGrossRowChanged(ProfitAndLossRow? value)
    {
        if (value is not null)
        {
            SelectedRow = value;
            SelectedIndirectRow = null;
        }
    }

    partial void OnSelectedIndirectRowChanged(ProfitAndLossRow? value)
    {
        if (value is not null)
        {
            SelectedRow = value;
            SelectedGrossRow = null;
        }
    }

    [ObservableProperty]
    private ObservableCollection<LedgerStatementRow> _drillDownRows = [];

    [ObservableProperty]
    private string _status = "Press Load for the current financial year.";

    [ObservableProperty]
    private bool _isBusy;

    public ProfitAndLossViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Ledger statement", LoadDrillDownCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Ctrl+Enter Ledger statement for the highlighted line · Esc Back";

    /// <summary>Ctrl+Enter: the postings behind the highlighted line, over the same period.</summary>
    [RelayCommand]
    private async Task LoadDrillDownAsync(CancellationToken cancellationToken)
    {
        if (SelectedRow is null)
        {
            Status = "Highlight a line first, then press Ctrl+Enter.";
            return;
        }

        IsBusy = true;

        try
        {
            var rows = await _reports.GetLedgerStatementAsync(
                SelectedRow.LedgerId,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                cancellationToken)
                .ConfigureAwait(true);

            DrillDownRows = new ObservableCollection<LedgerStatementRow>(rows);

            Status = $"{SelectedRow.LedgerName}: {DrillDownRows.Count} posting(s) in this period.";
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
