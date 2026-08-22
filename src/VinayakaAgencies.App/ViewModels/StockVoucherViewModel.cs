using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>One editable line of the stock voucher being entered.</summary>
public sealed partial class StockVoucherLineEditor : ObservableObject
{
    public StockVoucherLineEditor(ItemLookupRow item)
    {
        Item = item;
        _itemId = item.Id;
        _itemName = item.Name;
    }

    public ItemLookupRow Item { get; }

    [ObservableProperty]
    private Guid _itemId;

    [ObservableProperty]
    private string _itemName;

    [ObservableProperty]
    private StockMovementDirection _direction = StockMovementDirection.Increase;

    [ObservableProperty]
    private Guid? _godownId;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitCost = "0";

    [ObservableProperty]
    private string _batchNumber = string.Empty;

    /// <summary>Semicolon-separated serial numbers, parsed at post time.</summary>
    [ObservableProperty]
    private string _serialNumbers = string.Empty;

    public IReadOnlyList<string> ParsedSerials =>
        SerialNumbers
            .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}

/// <summary>
/// The stock voucher entry screen: opening balances, receipts, issues and inter-godown transfers
/// (phase-2-inventory.md §2). Posting runs through the application service, which refuses on
/// validation errors and asks the operator to confirm negative-stock warnings.
/// </summary>
public sealed partial class StockVoucherViewModel : ObservableObject
{
    private readonly IInventoryMasterService _masters;
    private readonly IStockVoucherService _stockVouchers;
    private readonly IChartOfAccountsService _chartOfAccounts;
    private readonly IStockReportService _reports;

    [ObservableProperty]
    private StockVoucherPurpose _purpose = StockVoucherPurpose.Receipt;

    [ObservableProperty]
    private DateTime _voucherDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<LedgerSummary> _ledgerChoices = [];

    [ObservableProperty]
    private LedgerSummary? _counterLedger;

    [ObservableProperty]
    private string _narration = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GodownSummary> _godownChoices = [];

    [ObservableProperty]
    private GodownSummary? _defaultGodown;

    [ObservableProperty]
    private string _itemSearchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ItemLookupRow> _itemChoices = [];

    [ObservableProperty]
    private ItemLookupRow? _selectedNewItem;

    [ObservableProperty]
    private ObservableCollection<StockVoucherLineEditor> _lines = [];

    [ObservableProperty]
    private StockVoucherLineEditor? _selectedLine;

    [ObservableProperty]
    private ObservableCollection<PostedStockVoucherSummary> _recentVouchers = [];

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public StockVoucherViewModel(
        IInventoryMasterService masters,
        IStockVoucherService stockVouchers,
        IChartOfAccountsService chartOfAccounts,
        IStockReportService reports)
    {
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
        _stockVouchers = stockVouchers ?? throw new ArgumentNullException(nameof(stockVouchers));
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    public IReadOnlyList<StockMovementDirection> DirectionChoices { get; } =
        [StockMovementDirection.Increase, StockMovementDirection.Decrease];

    /// <summary>The four purposes a manual stock voucher can carry.</summary>
    public IReadOnlyList<StockVoucherPurpose> PurposeChoices { get; } =
    [
        StockVoucherPurpose.OpeningBalance,
        StockVoucherPurpose.Receipt,
        StockVoucherPurpose.Issue,
        StockVoucherPurpose.Transfer,
    ];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            LedgerChoices = new ObservableCollection<LedgerSummary>(
                await _chartOfAccounts.ListLedgersAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            GodownChoices = new ObservableCollection<GodownSummary>(
                await _masters.ListGodownsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            DefaultGodown = GodownChoices.FirstOrDefault();

            await SearchItemsAsync(cancellationToken).ConfigureAwait(true);
            await LoadRecentAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Ready. {Lines.Count} line(s) staged.";
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
    private async Task SearchItemsAsync(CancellationToken cancellationToken)
    {
        var rows = await _reports.SearchItemsAsync(ItemSearchText, take: 100, cancellationToken)
            .ConfigureAwait(true);

        ItemChoices = new ObservableCollection<ItemLookupRow>(rows);
    }

    [RelayCommand]
    private void AddLine()
    {
        if (SelectedNewItem is null)
        {
            Status = "Pick an item first — type in the search box to find one.";
            return;
        }

        var line = new StockVoucherLineEditor(SelectedNewItem)
        {
            Direction = Purpose == StockVoucherPurpose.Issue
                ? StockMovementDirection.Decrease
                : StockMovementDirection.Increase,
            GodownId = DefaultGodown?.Id,
        };

        Lines.Add(line);
        SelectedLine = line;
        Status = $"{Lines.Count} line(s) staged.";
    }

    [RelayCommand]
    private void RemoveLine()
    {
        if (SelectedLine is not null)
        {
            Lines.Remove(SelectedLine);
            Status = $"{Lines.Count} line(s) staged.";
        }
    }

    [RelayCommand]
    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildRequest(out var request, out var problem))
        {
            Status = problem!;
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _stockVouchers.ValidateAsync(request!, cancellationToken)
                .ConfigureAwait(true);

            Status = result.HasErrors
                ? "Cannot post: " + string.Join(" | ", result.Errors)
                : result.HasWarnings
                    ? "Warnings (posting needs confirmation): " + string.Join(" | ", result.Warnings)
                    : "No problems found.";
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
    private async Task PostAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildRequest(out var request, out var problem))
        {
            Status = problem!;
            return;
        }

        IsBusy = true;

        try
        {
            var posted = await _stockVouchers.PostAsync(request!, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Posted {posted.Number}.";
            Lines.Clear();

            await LoadRecentAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("This voucher would take stock below zero", StringComparison.Ordinal))
        {
            if (System.Windows.MessageBox.Show(
                    ex.Message,
                    "Negative stock warning",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes)
            {
                IsBusy = false;
                await PostConfirmedAsync(request!, cancellationToken).ConfigureAwait(true);
                return;
            }

            Status = "Not posted — the warning was not confirmed.";
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

    private async Task PostConfirmedAsync(
        PostStockVoucherRequest request,
        CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var confirmed = request with { ConfirmNegativeStockWarnings = true };
            var posted = await _stockVouchers.PostAsync(confirmed, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Posted {posted.Number} (warnings confirmed).";
            Lines.Clear();

            await LoadRecentAsync(cancellationToken).ConfigureAwait(true);
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

    private bool TryBuildRequest(out PostStockVoucherRequest? request, out string? problem)
    {
        request = null;
        problem = null;

        if (Lines.Count == 0)
        {
            problem = "Add at least one line.";
            return false;
        }

        if (Purpose != StockVoucherPurpose.Transfer && CounterLedger is null)
        {
            problem = "Pick the counter ledger this movement's value comes from or goes to.";
            return false;
        }

        var lines = new List<StockVoucherLineRequest>();

        foreach (var editor in Lines)
        {
            if (!editor.GodownId.HasValue)
            {
                problem = $"Choose a godown for '{editor.ItemName}'.";
                return false;
            }

            if (!decimal.TryParse(editor.Quantity, out var quantity))
            {
                problem = $"The quantity for '{editor.ItemName}' must be a number.";
                return false;
            }

            if (!decimal.TryParse(editor.UnitCost, out var unitCost))
            {
                problem = $"The unit cost for '{editor.ItemName}' must be a number.";
                return false;
            }

            lines.Add(new StockVoucherLineRequest(
                editor.Direction,
                editor.ItemId,
                editor.GodownId.Value,
                quantity,
                UnitCost: editor.Direction == StockMovementDirection.Increase ? unitCost : 0m,
                BatchNumber: string.IsNullOrWhiteSpace(editor.BatchNumber) ? null : editor.BatchNumber.Trim(),
                SerialNumbers: editor.ParsedSerials.Count > 0 ? editor.ParsedSerials : null));
        }

        request = new PostStockVoucherRequest(
            Purpose,
            DateOnly.FromDateTime(VoucherDate),
            lines,
            Narration,
            CounterLedgerId: CounterLedger?.Id,
            ConfirmNegativeStockWarnings: false);

        return true;
    }

    private async Task LoadRecentAsync(CancellationToken cancellationToken)
    {
        var recent = await _stockVouchers.ListRecentAsync(take: 50, cancellationToken)
            .ConfigureAwait(true);

        RecentVouchers = new ObservableCollection<PostedStockVoucherSummary>(recent);
    }
}
