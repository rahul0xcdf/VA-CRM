using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The balance sheet as of a date: assets against liabilities, with the derived net profit carried
/// into the Profit &amp; Loss A/c line (spec §8.4).
/// </summary>
/// <remarks>
/// <c>Ctrl+Enter</c> on a line opens that ledger's statement from the start of the financial year to
/// the as-on date (ADR-0007) — the window a closing balance is actually built from.
/// </remarks>
public sealed partial class BalanceSheetViewModel : ObservableObject, IScreenShortcuts
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
    private BalanceSheetRow? _selectedAsset;

    [ObservableProperty]
    private BalanceSheetRow? _selectedLiability;

    /// <summary>
    /// The one highlighted row, whichever side it is on.
    /// </summary>
    /// <remarks>
    /// Each grid keeps its own selection and setting one clears the other; binding both to a single
    /// property does not work, because each grid nulls a row that is not in its own collection. See
    /// <see cref="ProfitAndLossViewModel"/>, which has the same shape.
    /// </remarks>
    [ObservableProperty]
    private BalanceSheetRow? _selectedRow;

    partial void OnSelectedAssetChanged(BalanceSheetRow? value)
    {
        if (value is not null)
        {
            SelectedRow = value;
            SelectedLiability = null;
        }
    }

    partial void OnSelectedLiabilityChanged(BalanceSheetRow? value)
    {
        if (value is not null)
        {
            SelectedRow = value;
            SelectedAsset = null;
        }
    }

    [ObservableProperty]
    private ObservableCollection<LedgerStatementRow> _drillDownRows = [];

    [ObservableProperty]
    private string _status = "Press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public BalanceSheetViewModel(IAccountingReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Ledger statement", LoadDrillDownCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Ctrl+Enter Ledger statement for the highlighted line · Esc Back";

    /// <summary>Ctrl+Enter: the postings behind the highlighted closing balance.</summary>
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
            var asOn = DateOnly.FromDateTime(AsOnDate);

            var rows = await _reports.GetLedgerStatementAsync(
                SelectedRow.LedgerId,
                FinancialYearStart(asOn),
                asOn,
                cancellationToken)
                .ConfigureAwait(true);

            DrillDownRows = new ObservableCollection<LedgerStatementRow>(rows);

            Status = $"{SelectedRow.LedgerName}: {DrillDownRows.Count} posting(s) this financial year. " +
                     "Anything before it is carried in as the opening balance.";
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

    /// <summary>
    /// 1 April of the financial year <paramref name="asOn"/> falls in.
    /// </summary>
    /// <remarks>
    /// The month is the Indian standard confirmed in <c>open-questions.md</c> #9 and stored on the
    /// company. It is repeated here rather than read from the company because this view model has no
    /// company dependency and the drill-down is a reading aid, not a posted figure — the balance
    /// itself comes from the report service, which does use the stored month.
    /// </remarks>
    private static DateOnly FinancialYearStart(DateOnly asOn) =>
        new(asOn.Month >= 4 ? asOn.Year : asOn.Year - 1, 4, 1);

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
