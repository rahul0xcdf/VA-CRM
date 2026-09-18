using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
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

    /// <summary>
    /// The godown itself, not its id (ADR-0007).
    /// </summary>
    /// <remarks>
    /// A lookup field selects an object. Holding the id here would mean looking the name back up on
    /// every render to show what the row already knows.
    /// </remarks>
    [ObservableProperty]
    private GodownSummary? _godown;

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
/// <remarks>
/// <para>
/// Reworked to the ADR-0007 entry pattern. The purpose was a combo box; it is now <c>Alt+F9</c>
/// receipt, <c>Alt+F8</c> issue, <c>Alt+F7</c> transfer and <c>F10</c> opening balance, advertised on
/// the button bar with the current one marked.
/// </para>
/// <para>
/// The item picker keeps its server-side search — the Phase 2 checkpoint requires this to stay usable
/// at five thousand SKUs, and loading every item into the client to filter it would trade one problem
/// for a worse one. The lookup types straight into that search and ranks what comes back, so the
/// operator sees one field rather than a search box, a button and a list.
/// </para>
/// </remarks>
public sealed partial class StockVoucherViewModel : ObservableObject, IScreenShortcuts
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

    // --- Create-an-item-without-leaving-the-voucher (Alt+C) ---

    [ObservableProperty]
    private bool _isCreatingItem;

    [ObservableProperty]
    private string _newItemName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<UnitOfMeasureSummary> _unitChoices = [];

    [ObservableProperty]
    private UnitOfMeasureSummary? _newItemUnit;

    [ObservableProperty]
    private ObservableCollection<StockGroupSummary> _stockGroupChoices = [];

    [ObservableProperty]
    private StockGroupSummary? _newItemGroup;

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

    // --- The shell's chrome (ADR-0006 contract, ADR-0007 content) ---

    /// <summary>Ctrl+A. The create-an-item band owns Accept while it is open, as Tally's does.</summary>
    ICommand? IScreenShortcuts.AcceptCommand => IsCreatingItem ? ConfirmCreateItemCommand : PostCommand;

    /// <summary>Alt+X — abandon what is staged, not anything posted.</summary>
    ICommand? IScreenShortcuts.CancelCommand =>
        IsCreatingItem ? CancelCreateItemCommand : ClearEntryCommand;

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ButtonBar =>
    [
        .. VoucherTypeKeyMap.KeyedStockPurposes.Select(purpose => new ShortcutButton(
            VoucherTypeKeyMap.For(purpose)!,
            VoucherTypeKeyMap.LabelFor(purpose),
            SelectPurposeCommand,
            purpose,
            IsCurrent: Purpose == purpose)),
    ];

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "Create item", BeginCreateItemCommand),
        new ShortcutButton(AppShortcuts.RemoveLine, "Remove line", RemoveLineCommand, SelectedLine),
        new ShortcutButton(AppShortcuts.Find, "Validate", ValidateCommand),
    ];

    string? IScreenShortcuts.StatusBarHints => IsCreatingItem
        ? "Ctrl+A Save item · Alt+X Abandon it"
        : "Alt+F9/F8/F7 Purpose · Alt+C Create item · Ctrl+A Post · Ctrl+D Remove line";

    /// <summary>True once the new item has a name and a unit, so the create band can be accepted.</summary>
    public bool CanConfirmCreateItem =>
        IsCreatingItem
        && !string.IsNullOrWhiteSpace(NewItemName)
        && NewItemUnit is not null;

    public IReadOnlyList<StockMovementDirection> DirectionChoices { get; } =
        [StockMovementDirection.Increase, StockMovementDirection.Decrease];

    // The purpose list that used to feed a combo box is gone: the four purposes are on Alt+F9 /
    // Alt+F8 / Alt+F7 / F10, and VoucherTypeKeyMap.KeyedStockPurposes is the one enumeration of them.

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

            // Loaded here rather than when Alt+C is first pressed: the create band must open filled,
            // or the gesture that exists to save a round trip costs one.
            UnitChoices = new ObservableCollection<UnitOfMeasureSummary>(
                await _masters.ListUnitsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            StockGroupChoices = new ObservableCollection<StockGroupSummary>(
                await _masters.ListStockGroupsAsync(cancellationToken: cancellationToken)
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
            Godown = DefaultGodown,
        };

        Lines.Add(line);
        SelectedLine = line;
        Status = $"{Lines.Count} line(s) staged.";
    }

    /// <summary>
    /// Ctrl+D. Falls back to the selected line when the shell invokes it from the action bar, so the
    /// key works from anywhere on the screen rather than only from inside the grid.
    /// </summary>
    [RelayCommand]
    private void RemoveLine(StockVoucherLineEditor? line)
    {
        var target = line ?? SelectedLine;

        if (target is null)
        {
            Status = "Select the line to remove first.";
            return;
        }

        Lines.Remove(target);

        if (ReferenceEquals(SelectedLine, target))
        {
            SelectedLine = null;
        }

        Status = $"{Lines.Count} line(s) staged.";
    }

    /// <summary>
    /// Alt+F9 / Alt+F8 / Alt+F7 / F10: switch what this voucher is for.
    /// </summary>
    /// <remarks>
    /// Staged lines survive the switch, and their direction is re-defaulted to match: an issue voucher
    /// whose lines still said "Increase" would post the opposite of what the operator asked for. Lines
    /// the operator had deliberately flipped are re-defaulted too, which is the lesser evil — the
    /// direction is visible on every line, whereas a silent mixed-direction voucher is not.
    /// </remarks>
    [RelayCommand]
    private void SelectPurpose(StockVoucherPurpose purpose)
    {
        if (Purpose == purpose)
        {
            return;
        }

        Purpose = purpose;

        var direction = purpose == StockVoucherPurpose.Issue
            ? StockMovementDirection.Decrease
            : StockMovementDirection.Increase;

        foreach (var line in Lines)
        {
            line.Direction = direction;
        }

        Status = $"{VoucherTypeKeyMap.LabelFor(purpose)}. " +
                 $"{Lines.Count} line(s) kept, direction set to {direction}.";
    }

    /// <summary>Alt+X — clear what is staged. Nothing posted is touched.</summary>
    [RelayCommand]
    private void ClearEntry()
    {
        Lines.Clear();
        SelectedLine = null;
        Narration = string.Empty;
        ItemSearchText = string.Empty;
        SelectedNewItem = null;

        Status = "Entry cleared. Nothing posted was touched.";
    }

    // --- Alt+C: create the item from inside the voucher ---

    [RelayCommand]
    private void BeginCreateItem(object? typedName)
    {
        NewItemName = typedName as string ?? ItemSearchText;
        NewItemUnit = UnitChoices.FirstOrDefault();
        NewItemGroup = null;
        IsCreatingItem = true;

        Status = UnitChoices.Count == 0
            ? "Press Refresh first — the unit list has not been loaded."
            : "New item: name it, choose its unit, then Ctrl+A.";
    }

    [RelayCommand(CanExecute = nameof(CanConfirmCreateItem))]
    private async Task ConfirmCreateItemAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var created = await _masters.SaveItemAsync(
                new SaveItemRequest(
                    Id: null,
                    Name: NewItemName.Trim(),
                    BaseUnitId: NewItemUnit!.Id,
                    StockGroupId: NewItemGroup?.Id),
                cancellationToken)
                .ConfigureAwait(true);

            CloseCreateItem();

            // Re-read through the same search the lookup uses, so the row on the line is the row the
            // query returns rather than one hand-assembled here that could disagree with it.
            ItemSearchText = created.Name;

            await SearchItemsAsync(cancellationToken).ConfigureAwait(true);

            SelectedNewItem = ItemChoices.FirstOrDefault(item => item.Id == created.Id);

            Status = SelectedNewItem is null
                ? $"Created '{created.Name}', but it did not come back from the item search."
                : $"Created '{created.Name}'. Press Add line to put it on the voucher.";
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
    private void CancelCreateItem()
    {
        CloseCreateItem();
        Status = "Item abandoned. The voucher is untouched.";
    }

    private void CloseCreateItem()
    {
        IsCreatingItem = false;
        NewItemName = string.Empty;
        NewItemGroup = null;
    }

    /// <summary>
    /// Typing in the lookup re-runs the server-side search.
    /// </summary>
    /// <remarks>
    /// Fired per keystroke against a local SQLite file with an indexed name column and a hard take of
    /// 100. Debouncing was considered and left out: it would add a timer and a race for a query that
    /// costs less than the keystroke that triggered it.
    /// </remarks>
    partial void OnItemSearchTextChanged(string value) => SearchItemsCommand.Execute(null);

    partial void OnPurposeChanged(StockVoucherPurpose value) => NotifyBarsChanged();

    partial void OnSelectedLineChanged(StockVoucherLineEditor? value) => NotifyBarsChanged();

    partial void OnIsCreatingItemChanged(bool value)
    {
        ConfirmCreateItemCommand.NotifyCanExecuteChanged();
        NotifyBarsChanged();
    }

    partial void OnNewItemNameChanged(string value) => ConfirmCreateItemCommand.NotifyCanExecuteChanged();

    partial void OnNewItemUnitChanged(UnitOfMeasureSummary? value) =>
        ConfirmCreateItemCommand.NotifyCanExecuteChanged();

    /// <summary>Tells the shell to re-read both bars; see VoucherEntryViewModel for why it matters.</summary>
    private void NotifyBarsChanged()
    {
        OnPropertyChanged(nameof(IScreenShortcuts.ButtonBar));
        OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));
        OnPropertyChanged(nameof(IScreenShortcuts.StatusBarHints));
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
            if (editor.Godown is null)
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
                editor.Godown.Id,
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
