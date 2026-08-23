using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Manual bank reconciliation: the statement of one bank ledger's lines, with marks added and
/// withdrawn by hand (spec §12). Statement import is a later phase.
/// </summary>
public sealed partial class BankReconciliationViewModel : ObservableObject
{
    private readonly IBankReconciliationService _reconciliation;
    private readonly IChartOfAccountsService _chartOfAccounts;

    [ObservableProperty]
    private ObservableCollection<LedgerSummary> _bankLedgers = [];

    [ObservableProperty]
    private LedgerSummary? _selectedLedger;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddMonths(-2);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<BankReconciliationLine> _lines = [];

    [ObservableProperty]
    private BankReconciliationLine? _selectedLine;

    [ObservableProperty]
    private DateTime _bankDate = DateTime.Today;

    [ObservableProperty]
    private string _status = "Pick a bank ledger and press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public BankReconciliationViewModel(
        IBankReconciliationService reconciliation,
        IChartOfAccountsService chartOfAccounts)
    {
        _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
    }

    [RelayCommand]
    private async Task LoadListsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var banks = await _chartOfAccounts.ListBankLedgersAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            BankLedgers = new ObservableCollection<LedgerSummary>(banks);

            Status = banks.Count == 0
                ? "No bank ledgers yet — create one under 'Bank Accounts' in the ledger master."
                : $"{banks.Count} bank ledger(s) available.";

            if (SelectedLedger is null && banks.Count > 0)
            {
                SelectedLedger = banks[0];
            }
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
    private async Task LoadStatementAsync(CancellationToken cancellationToken)
    {
        if (SelectedLedger is null)
        {
            Status = "Choose a bank ledger first.";
            return;
        }

        IsBusy = true;

        try
        {
            var rows = await _reconciliation.GetStatementAsync(
                SelectedLedger.Id,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                cancellationToken)
                .ConfigureAwait(true);

            Lines = new ObservableCollection<BankReconciliationLine>(rows);

            var cleared = rows.Count(row => row.IsCleared);

            Status = $"{Lines.Count} line(s), {cleared} cleared. An altered voucher reappears " +
                     "unreconciled on purpose — re-confirm it against the bank.";
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
    private async Task MarkClearedAsync(CancellationToken cancellationToken)
    {
        if (SelectedLine is null)
        {
            Status = "Select the line that cleared.";
            return;
        }

        IsBusy = true;

        try
        {
            await _reconciliation.MarkClearedAsync(
                SelectedLine.JournalLineId,
                DateOnly.FromDateTime(BankDate),
                cancellationToken)
                .ConfigureAwait(true);

            await LoadStatementCommand.ExecuteAsync(null).ConfigureAwait(true);
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
    private async Task UnmarkClearedAsync(CancellationToken cancellationToken)
    {
        if (SelectedLine is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _reconciliation.UnmarkClearedAsync(SelectedLine.JournalLineId, cancellationToken)
                .ConfigureAwait(true);

            await LoadStatementCommand.ExecuteAsync(null).ConfigureAwait(true);
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
