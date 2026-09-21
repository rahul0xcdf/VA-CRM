using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>One editable line of the trade document being entered.</summary>
/// <remarks>
/// Carries the phase-4 pricing evidence alongside the keyed rate: what the engine applied and
/// from which tier, whether the operator moved the final rate off it (logged at save as C4.2),
/// and whether the override gate locks this cell (C4.5).
/// </remarks>
public sealed partial class TradeDocumentLineEditor : ObservableObject
{
    private bool _suppressRateNotifications;

    public TradeDocumentLineEditor(ItemLookupRow item)
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

    /// <summary>
    /// The godown itself, not its id (ADR-0007).
    /// </summary>
    /// <remarks>
    /// A lookup field selects an object. Holding the id here would mean resolving the name back on
    /// every render to show what the row already knows.
    /// </remarks>
    [ObservableProperty]
    private GodownSummary? _godown;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _rate = "0";

    [ObservableProperty]
    private string _discountPercent = "0";

    [ObservableProperty]
    private string _batchNumber = string.Empty;

    /// <summary>Semicolon-separated serial numbers, parsed at save/post time.</summary>
    [ObservableProperty]
    private string _serialNumbers = string.Empty;

    /// <summary>What the pricing engine applied; null on a hand-keyed line.</summary>
    public long? AppliedRateMinor { get; private set; }

    public SalePriceSource PriceSource { get; private set; } = SalePriceSource.Manual;

    [ObservableProperty]
    private bool _isPriceOverridden;

    /// <summary>True when the override gate is off and an auto-applied price fills this cell.</summary>
    [ObservableProperty]
    private bool _isRateLocked;

    /// <summary>The tier label shown in the grid; empty for hand-keyed lines.</summary>
    public string PriceSourceLabel => PriceSource switch
    {
        SalePriceSource.CustomerSpecialPrice => "customer",
        SalePriceSource.CategoryPriceList => "category",
        SalePriceSource.ItemDefault => "item default",
        _ => string.Empty,
    };

    public IReadOnlyList<string> ParsedSerials =>
        SerialNumbers
            .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Fills the rate from a resolved price, marking the line auto-priced (C4.1).</summary>
    public void ApplyResolved(ResolvedSalePrice resolved)
    {
        _suppressRateNotifications = true;

        try
        {
            Rate = MoneyScale.FromMinor(resolved.RateMinor).ToString("0.####", CultureInfo.InvariantCulture);
            AppliedRateMinor = resolved.RateMinor;
            PriceSource = resolved.Source;
            IsPriceOverridden = false;
        }
        finally
        {
            _suppressRateNotifications = false;
        }

        OnPropertyChanged(nameof(PriceSourceLabel));
    }

    /// <summary>Restores saved evidence so an edit-and-save cycle never launders an override.</summary>
    public void RestoreEvidence(long? appliedRateMinor, bool isPriceOverridden, SalePriceSource priceSource)
    {
        _suppressRateNotifications = true;

        try
        {
            AppliedRateMinor = appliedRateMinor;
            PriceSource = priceSource;
            IsPriceOverridden = isPriceOverridden;
        }
        finally
        {
            _suppressRateNotifications = false;
        }

        OnPropertyChanged(nameof(PriceSourceLabel));
    }

    partial void OnRateChanged(string value)
    {
        if (_suppressRateNotifications || AppliedRateMinor is null)
        {
            return;
        }

        // Moving the rate off the applied one is the override C4.2 logs; returning to it clears.
        IsPriceOverridden = decimal.TryParse(value, out var rate)
            && MoneyScale.ToMinor(MoneyScale.Round(rate)) != AppliedRateMinor.Value;
    }
}

/// <summary>
/// The documents screen: quotations through tax invoices and their purchase mirror, raised,
/// converted, posted, reversed and printed from one place (phase-3-sales-purchase-pipeline.md §1).
/// </summary>
/// <remarks>
/// Posting runs through the application service, which posts journal voucher plus stock movements
/// atomically (C3.3) and refuses until negative-stock warnings are confirmed — exactly the manual
/// stock voucher's contract.
/// </remarks>
public sealed partial class TradeDocumentsViewModel : ObservableObject, IScreenShortcuts
{
    /// <summary>Ctrl+A. The create-an-item band owns Accept while it is open (ADR-0007).</summary>
    ICommand? IScreenShortcuts.AcceptCommand => IsCreatingItem ? ConfirmCreateItemCommand : SaveDraftCommand;

    /// <summary>Alt+X — abandon the draft being staged. Nothing saved is touched.</summary>
    ICommand? IScreenShortcuts.CancelCommand =>
        IsCreatingItem ? CancelCreateItemCommand : StartNewDocumentCommand;

    /// <summary>
    /// One key per document kind, on TallyPrime's published assignments (ADR-0007).
    /// </summary>
    /// <remarks>
    /// A quotation has no published Tally key, so it takes F10 — "the other voucher types" — rather
    /// than a key invented here that would not transfer and would spend a gesture a later phase may
    /// need. <see cref="VoucherTypeKeyMap.UnkeyedDocumentKinds"/> is the list F10 covers.
    /// </remarks>
    IReadOnlyList<ShortcutButton> IScreenShortcuts.ButtonBar =>
    [
        .. VoucherTypeKeyMap.KeyedDocumentKinds.Select(kind => new ShortcutButton(
            VoucherTypeKeyMap.For(kind)!,
            VoucherTypeKeyMap.LabelFor(kind),
            SelectDocumentKindCommand,
            kind,
            IsCurrent: NewDocumentKind == kind)),

        new ShortcutButton(
            AppShortcuts.OtherVouchers,
            VoucherTypeKeyMap.LabelFor(VoucherTypeKeyMap.UnkeyedDocumentKinds[0]),
            SelectDocumentKindCommand,
            VoucherTypeKeyMap.UnkeyedDocumentKinds[0],
            IsCurrent: VoucherTypeKeyMap.UnkeyedDocumentKinds.Contains(NewDocumentKind)),
    ];

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "Create item", BeginCreateItemCommand),
        new ShortcutButton(AppShortcuts.RemoveLine, "Remove line", RemoveLineCommand, SelectedLine),
        new ShortcutButton(AppShortcuts.Print, "Print", PrintCommand),
        new ShortcutButton(AppShortcuts.Delete, "Delete draft", DeleteDocumentCommand),
    ];

    string? IScreenShortcuts.StatusBarHints => IsCreatingItem
        ? "Ctrl+A Save item · Alt+X Abandon it"
        : "F8 Sales · F9 Purchase · Ctrl+F8/F9 Orders · Alt+C Create item · Ctrl+A Save draft";

    private readonly ITradeDocumentService _pipeline;
    private readonly IChartOfAccountsService _chartOfAccounts;
    private readonly IInventoryMasterService _masters;
    private readonly IStockReportService _reports;
    private readonly IPrintTemplateService _printTemplates;
    private readonly ICompanyService _companies;
    private readonly ICompanyContext _companyContext;
    private readonly ICustomerService _customers;
    private readonly IPricingService _pricing;
    private readonly ISalesSettingsService _salesSettings;
    private readonly ICreditLimitService _creditLimits;

    private Dictionary<Guid, CustomerSummary> _customersByLedgerId = [];

    private CustomerSummary? _activeCustomer;

    private bool _allowPriceOverride = true;

    /// <summary>True while the party combo is being assigned programmatically — no auto-pricing.</summary>
    private bool _suppressPartyReprice;

    [ObservableProperty]
    private DateTime _documentDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<LedgerSummary> _ledgerChoices = [];

    [ObservableProperty]
    private LedgerSummary? _partyLedger;

    [ObservableProperty]
    private string _narration = string.Empty;

    [ObservableProperty]
    private string _partyReferenceNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _partyReferenceDate;

    [ObservableProperty]
    private Guid? _editingDocumentId;

    [ObservableProperty]
    private string _editingCaption = "New quotation";

    [ObservableProperty]
    private string _editingStatus = string.Empty;

    [ObservableProperty]
    private bool _isEditingEnabled = true;

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
    private ObservableCollection<TradeDocumentLineEditor> _lines = [];

    [ObservableProperty]
    private TradeDocumentLineEditor? _selectedLine;

    [ObservableProperty]
    private ObservableCollection<TradeDocumentSummary> _documents = [];

    [ObservableProperty]
    private TradeDocumentSummary? _selectedDocument;

    [ObservableProperty]
    private IReadOnlyList<TradeDocumentKind> _convertTargets = [];

    [ObservableProperty]
    private TradeDocumentKind _selectedTargetKind;

    [ObservableProperty]
    private GodownSummary? _convertGodown;

    [ObservableProperty]
    private DateTime? _convertDate = DateTime.Today;

    [ObservableProperty]
    private string _cancellationReason = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    // --- Create-an-item-without-leaving-the-document (Alt+C) ---

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

    public TradeDocumentsViewModel(
        ITradeDocumentService documents,
        IChartOfAccountsService chartOfAccounts,
        IInventoryMasterService masters,
        IStockReportService reports,
        IPrintTemplateService templates,
        ICompanyService companies,
        ICompanyContext companyContext,
        ICustomerService customers,
        IPricingService pricing,
        ISalesSettingsService salesSettings,
        ICreditLimitService creditLimits)
    {
        _pipeline = documents ?? throw new ArgumentNullException(nameof(documents));
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _printTemplates = templates ?? throw new ArgumentNullException(nameof(templates));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
        _salesSettings = salesSettings ?? throw new ArgumentNullException(nameof(salesSettings));
        _creditLimits = creditLimits ?? throw new ArgumentNullException(nameof(creditLimits));
    }

    // The kind list that used to feed a combo box is gone: the seven kinds are on F8 / F9 / Ctrl+F8 /
    // Ctrl+F9 / Alt+F8 / Alt+F9 / F10, and VoucherTypeKeyMap's Keyed- and UnkeyedDocumentKinds are the
    // one enumeration of them.

    /// <summary>The kind the next new document will carry.</summary>
    [ObservableProperty]
    private TradeDocumentKind _newDocumentKind = TradeDocumentKind.Quotation;

    /// <summary>True once the new item has a name and a unit, so the create band can be accepted.</summary>
    public bool CanConfirmCreateItem =>
        IsCreatingItem
        && !string.IsNullOrWhiteSpace(NewItemName)
        && NewItemUnit is not null;

    partial void OnNewDocumentKindChanged(TradeDocumentKind value)
    {
        EditingCaption = $"New {value}";
        NotifyBarsChanged();
    }

    /// <summary>
    /// Typing in the item lookup re-runs the server-side search.
    /// </summary>
    /// <remarks>
    /// The search stays on the server because the Phase 2 checkpoint requires this to work at five
    /// thousand SKUs; the lookup only ranks the hundred rows that come back.
    /// </remarks>
    partial void OnItemSearchTextChanged(string value) => SearchItemsCommand.Execute(null);

    partial void OnSelectedLineChanged(TradeDocumentLineEditor? value) => NotifyBarsChanged();

    partial void OnEditingDocumentIdChanged(Guid? value) => NotifyBarsChanged();

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

    partial void OnPartyLedgerChanged(LedgerSummary? value)
    {
        // C4.1: picking the customer auto-fills their prices on staged lines of an UNSAVED
        // document. Loaded drafts keep their quoted prices — repricing saved lines would move
        // amounts that were already agreed (§4a) — and programmatic assignment never fires it.
        if (_suppressPartyReprice
            || EditingDocumentId.HasValue
            || !IsEditingEnabled
            || !TradeDocument.IsSalesSide(NewDocumentKind))
        {
            return;
        }

        _activeCustomer = value is not null
            && _customersByLedgerId.TryGetValue(value.Id, out var customer)
                ? customer
                : null;

        _ = RepriceStagedLinesAsync();
    }

    private async Task RepriceStagedLinesAsync()
    {
        if (_activeCustomer is null || Lines.Count == 0)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var companyId = await _companyContext.EnsureCompanyAsync().ConfigureAwait(true);

            foreach (var line in Lines.Where(line => !line.IsPriceOverridden))
            {
                var resolved = await _pricing.ResolveForSaleAsync(
                        companyId,
                        _activeCustomer.Id,
                        line.ItemId)
                    .ConfigureAwait(true);

                if (resolved is not null)
                {
                    line.ApplyResolved(resolved);
                }
            }

            ApplyOverrideGate();
            Status = $"Prices applied for {_activeCustomer.Name}.";
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

    /// <summary>Locks every auto-priced rate cell when the override gate is off (C4.5).</summary>
    private void ApplyOverrideGate()
    {
        foreach (var line in Lines)
        {
            line.IsRateLocked = !_allowPriceOverride && line.AppliedRateMinor.HasValue;
        }
    }

    partial void OnSelectedDocumentChanged(TradeDocumentSummary? value)
    {
        ConvertTargets = value is null ? [] : TargetsFor(value.Kind);
        SelectedTargetKind = ConvertTargets.FirstOrDefault();

        var editable = value is not null && value.Status is TradeDocumentStatus.Draft or TradeDocumentStatus.Open;
        IsEditingEnabled = false;
        EditingStatus = value is null
            ? string.Empty
            : $"{value.Kind} {value.DisplayNumber} — {value.Status}. "
              + (editable ? "Load to edit." : "Loaded documents are read-only here.");
    }

    private static IReadOnlyList<TradeDocumentKind> TargetsFor(TradeDocumentKind source) => source switch
    {
        TradeDocumentKind.Quotation => [TradeDocumentKind.SalesOrder, TradeDocumentKind.TaxInvoice],
        TradeDocumentKind.SalesOrder => [TradeDocumentKind.DeliveryChallan, TradeDocumentKind.TaxInvoice],
        TradeDocumentKind.DeliveryChallan => [TradeDocumentKind.TaxInvoice],
        TradeDocumentKind.PurchaseOrder => [TradeDocumentKind.GoodsReceipt, TradeDocumentKind.PurchaseInvoice],
        TradeDocumentKind.GoodsReceipt => [TradeDocumentKind.PurchaseInvoice],
        _ => [],
    };

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

            // Loaded here rather than on the first Alt+C, so the create band opens ready to type.
            UnitChoices = new ObservableCollection<UnitOfMeasureSummary>(
                await _masters.ListUnitsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            StockGroupChoices = new ObservableCollection<StockGroupSummary>(
                await _masters.ListStockGroupsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            var customers = await _customers.ListAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            _customersByLedgerId = customers.ToDictionary(customer => customer.LedgerId);

            _activeCustomer = _activeCustomer is not null
                && _customersByLedgerId.TryGetValue(_activeCustomer.LedgerId, out var refreshed)
                    ? refreshed
                    : null;

            var settings = await _salesSettings.GetAsync(cancellationToken).ConfigureAwait(true);
            _allowPriceOverride = settings.AllowPriceOverride;

            DefaultGodown ??= GodownChoices.FirstOrDefault();
            ConvertGodown ??= DefaultGodown;

            ApplyOverrideGate();
            await SearchItemsAsync(cancellationToken).ConfigureAwait(true);
            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);

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
    private async Task AddLineAsync()
    {
        if (!IsEditingEnabled)
        {
            Status = "This document is read-only; load an editable one or start a new document.";
            return;
        }

        if (SelectedNewItem is null)
        {
            Status = "Pick an item first — type in the search box to find one.";
            return;
        }

        var needsGodown = TradeDocument.RequiresGodownPerLine(NewDocumentKind);

        if (needsGodown && DefaultGodown is null)
        {
            Status = "This document moves stock, so pick a default godown first.";
            return;
        }

        var line = new TradeDocumentLineEditor(SelectedNewItem)
        {
            Godown = needsGodown ? DefaultGodown : null,
        };

        // C4.1: a new line prices itself from the selected customer's tiers as soon as it lands.
        if (_activeCustomer is not null && TradeDocument.IsSalesSide(NewDocumentKind))
        {
            try
            {
                var companyId = await _companyContext.EnsureCompanyAsync().ConfigureAwait(true);
                var resolved = await _pricing.ResolveForSaleAsync(companyId, _activeCustomer.Id, line.ItemId)
                    .ConfigureAwait(true);

                if (resolved is not null)
                {
                    line.ApplyResolved(resolved);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Status = ex.Message;
            }
        }

        Lines.Add(line);
        SelectedLine = line;
        Status = $"{Lines.Count} line(s) staged.";
    }

    /// <summary>
    /// Ctrl+D. Falls back to the selected line when the shell invokes it from the action bar, so the
    /// key works from anywhere on the screen and not only from inside the grid.
    /// </summary>
    [RelayCommand]
    private void RemoveLine(TradeDocumentLineEditor? line)
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
    /// F8 / F9 / Ctrl+F8 / Ctrl+F9 / Alt+F8 / Alt+F9 / F10: choose what is being raised.
    /// </summary>
    /// <remarks>
    /// Refuses while a saved document is loaded rather than silently re-labelling it. Changing the
    /// kind of a document that already has a number would either orphan that number or re-issue it
    /// from another series, and neither is something a function key should be able to do by accident.
    /// </remarks>
    [RelayCommand]
    private void SelectDocumentKind(TradeDocumentKind kind)
    {
        if (EditingDocumentId.HasValue)
        {
            Status = "A saved document is loaded. Press Alt+X to start a new one, then choose its kind.";
            return;
        }

        if (NewDocumentKind == kind)
        {
            return;
        }

        NewDocumentKind = kind;
        Status = $"Staging a {VoucherTypeKeyMap.LabelFor(kind)}. {Lines.Count} line(s) kept.";
    }

    // --- Alt+C: create the item from inside the document ---

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

            // Re-read through the same search the lookup uses, so the row that lands on the line is
            // the row the query returns rather than one hand-assembled here.
            ItemSearchText = created.Name;

            await SearchItemsAsync(cancellationToken).ConfigureAwait(true);

            SelectedNewItem = ItemChoices.FirstOrDefault(item => item.Id == created.Id);

            Status = SelectedNewItem is null
                ? $"Created '{created.Name}', but it did not come back from the item search."
                : $"Created '{created.Name}'. Press Add line to put it on the document.";
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
        Status = "Item abandoned. The document is untouched.";
    }

    private void CloseCreateItem()
    {
        IsCreatingItem = false;
        NewItemName = string.Empty;
        NewItemGroup = null;
    }

    [RelayCommand]
    private void StartNewDocument()
    {
        EditingDocumentId = null;
        EditingCaption = $"New {NewDocumentKind}";
        EditingStatus = string.Empty;
        IsEditingEnabled = true;
        Lines.Clear();
        Narration = string.Empty;
        PartyReferenceNumber = string.Empty;
        PartyReferenceDate = null;
        DocumentDate = DateTime.Today;
        Status = $"Staging a new {NewDocumentKind}.";
    }

    [RelayCommand]
    private async Task SaveDraftAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildRequest(out var request, out var problem))
        {
            Status = problem!;
            return;
        }

        // Credit terms apply to new orders and invoices (spec §2) — quotations and challans
        // promise nothing yet.
        if (!await ConfirmCreditAsync(request!, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;

        try
        {
            if (EditingDocumentId.HasValue)
            {
                var updated = await _pipeline.UpdateDraftAsync(
                        EditingDocumentId.Value,
                        request!,
                        cancellationToken)
                    .ConfigureAwait(true);

                Status = $"Saved {updated.Kind} {updated.DisplayNumber}.";
            }
            else
            {
                var created = await _pipeline.CreateAsync(request!, cancellationToken)
                    .ConfigureAwait(true);

                Status = $"Created {created.Kind} {created.DisplayNumber}.";
                EditingDocumentId = created.Id;
                EditingStatus = $"{created.Kind} {created.DisplayNumber} — {created.Status}.";
            }

            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task LoadSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedDocument is null)
        {
            Status = "Pick a document in the list first.";
            return;
        }

        IsBusy = true;

        try
        {
            var detail = await _pipeline.GetAsync(SelectedDocument.Id, cancellationToken)
                .ConfigureAwait(true);

            if (detail is null)
            {
                Status = "That document no longer exists.";
                return;
            }

            EditingDocumentId = detail.Id;
            NewDocumentKind = detail.Kind;
            DocumentDate = detail.DocumentDate.ToDateTime(TimeOnly.MinValue);
            Narration = detail.Narration ?? string.Empty;
            PartyReferenceNumber = detail.PartyReferenceNumber ?? string.Empty;
            PartyReferenceDate = detail.PartyReferenceDate?.ToDateTime(TimeOnly.MinValue);

            // Restoring the party must not fire the auto-pricer against the loaded lines.
            _suppressPartyReprice = true;

            try
            {
                PartyLedger = LedgerChoices.FirstOrDefault(ledger => ledger.Id == detail.PartyLedgerId);
            }
            finally
            {
                _suppressPartyReprice = false;
            }

            _activeCustomer = PartyLedger is not null
                && _customersByLedgerId.TryGetValue(PartyLedger.Id, out var loadedParty)
                    ? loadedParty
                    : null;

            var itemIds = detail.Lines.Select(line => line.ItemId).ToList();
            var knownItems = ItemChoices.Where(row => itemIds.Contains(row.Id))
                .ToDictionary(row => row.Id);

            Lines = new ObservableCollection<TradeDocumentLineEditor>(
                detail.Lines.Select(line =>
                {
                    var row = knownItems.TryGetValue(line.ItemId, out var found)
                        ? found
                        : new ItemLookupRow(line.ItemId, line.ItemName, null, null, false, false, false, "?");

                    var editor = new TradeDocumentLineEditor(row)
                    {
                        Godown = GodownChoices.FirstOrDefault(godown => godown.Id == line.GodownId),
                        Quantity = line.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
                        Rate = line.Rate.ToString("0.####", CultureInfo.InvariantCulture),
                        DiscountPercent = line.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture),
                        BatchNumber = line.BatchNumber ?? string.Empty,
                        SerialNumbers = string.Join(";", line.SerialNumbers),
                    };

                    editor.RestoreEvidence(
                        line.AppliedRateMinor,
                        line.IsPriceOverridden,
                        line.PriceSource);

                    return editor;
                }));

            ApplyOverrideGate();

            EditingCaption = $"{detail.Kind} {detail.DisplayNumber}";
            EditingStatus = $"{detail.Kind} {detail.DisplayNumber} — {detail.Status}.";
            IsEditingEnabled = detail.Status is TradeDocumentStatus.Draft or TradeDocumentStatus.Open;
            Status = IsEditingEnabled
                ? "Loaded for editing."
                : "Loaded read-only: only drafts and open documents may change.";
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
    private async Task DeleteDocumentAsync(CancellationToken cancellationToken)
    {
        if (SelectedDocument is null)
        {
            Status = "Pick a document in the list first.";
            return;
        }

        IsBusy = true;

        try
        {
            await _pipeline.DeleteAsync(SelectedDocument.Id, "Deleted from the Documents screen", cancellationToken)
                .ConfigureAwait(true);

            Status = $"Deleted {SelectedDocument.Kind} {SelectedDocument.DisplayNumber}.";
            EditingDocumentId = null;
            Lines.Clear();

            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task ConvertAsync(CancellationToken cancellationToken)
    {
        if (SelectedDocument is null)
        {
            Status = "Pick the document to convert in the list first.";
            return;
        }

        if (SelectedTargetKind == default)
        {
            Status = "Pick what this document converts into.";
            return;
        }

        var needsGodown = TradeDocument.RequiresGodownPerLine(SelectedTargetKind);

        if (needsGodown && ConvertGodown is null)
        {
            Status = "The target document moves stock; pick a default godown for lines that had none.";
            return;
        }

        IsBusy = true;

        try
        {
            var converted = await _pipeline.ConvertAsync(
                    new ConvertTradeDocumentRequest(
                        SelectedDocument.Id,
                        SelectedTargetKind,
                        DateOnly.FromDateTime(ConvertDate ?? DateTime.Today),
                        needsGodown ? ConvertGodown!.Id : null),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = $"Converted into {converted.Kind} {converted.DisplayNumber}.";
            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task ValidateInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!RequireInvoice(out var invoiceProblem))
        {
            Status = invoiceProblem!;
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _pipeline.ValidateInvoiceAsync(SelectedDocument!.Id, cancellationToken)
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
    private async Task PostInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!RequireInvoice(out var invoiceProblem))
        {
            Status = invoiceProblem!;
            return;
        }

        IsBusy = true;

        try
        {
            var posted = await _pipeline.PostInvoiceAsync(SelectedDocument!.Id, confirmNegativeStockWarnings: false, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Posted {posted.DocumentNumber}: journal and stock updated together.";
            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("This invoice would take stock below zero", StringComparison.Ordinal))
        {
            if (MessageBox.Show(
                    ex.Message,
                    "Negative stock warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                IsBusy = false;
                await PostConfirmedAsync(cancellationToken).ConfigureAwait(true);
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

    private async Task PostConfirmedAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var posted = await _pipeline.PostInvoiceAsync(SelectedDocument!.Id, confirmNegativeStockWarnings: true, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Posted {posted.DocumentNumber} (warnings confirmed).";
            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task CancelPostedInvoiceAsync(CancellationToken cancellationToken)
    {
        if (SelectedDocument is null || SelectedDocument.Status != TradeDocumentStatus.Issued)
        {
            Status = "Only an issued invoice can be reversed. Pick one in the list first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CancellationReason))
        {
            Status = "Type why this invoice is being reversed — the edit log keeps the reason.";
            return;
        }

        var confirmation = MessageBox.Show(
            $"Reverse {SelectedDocument.Kind} {SelectedDocument.DisplayNumber}? A cancelling voucher "
            + "will be written and the goods returned to stock, all at once.",
            "Reverse invoice",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var cancelled = await _pipeline.CancelPostedInvoiceAsync(
                    SelectedDocument.Id,
                    CancellationReason.Trim(),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = $"Reversed {cancelled.Kind} {cancelled.DisplayNumber}; its number stays spent.";
            CancellationReason = string.Empty;
            await LoadDocumentsAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task PrintAsync(CancellationToken cancellationToken)
    {
        if (SelectedDocument is null)
        {
            Status = "Pick a document in the list first.";
            return;
        }

        IsBusy = true;

        try
        {
            var document = await _pipeline.GetAsync(SelectedDocument.Id, cancellationToken)
                .ConfigureAwait(true);

            if (document is null)
            {
                Status = "That document no longer exists.";
                return;
            }

            var defaultTemplate = await _printTemplates.ResolveDefaultAsync(document.Kind.ToPrintKind(), cancellationToken)
                .ConfigureAwait(true);

            var template = defaultTemplate is null
                ? FallbackTemplate(document.Kind)
                : await _printTemplates.GetAsync(defaultTemplate.Id, cancellationToken)
                    .ConfigureAwait(true)
                    ?? FallbackTemplate(document.Kind);

            var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken)
                .ConfigureAwait(true);
            var company = await _companies.GetAsync(companyId, cancellationToken).ConfigureAwait(true);

            new Views.PrintPreviewWindow(document, template, company).Show();
            Status = $"Previewing {document.Kind} {document.DisplayNumber}.";
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
    /// What prints before an operator has designed anything for a kind: a plain layout from the
    /// document's own data, so Print always works even on a fresh install.
    /// </summary>
    private static PrintTemplateDetail FallbackTemplate(TradeDocumentKind kind) =>
        new(
            Guid.Empty,
            kind.ToPrintKind(),
            "(built-in)",
            Description: null,
            Priority: int.MaxValue,
            CurrentVersion: 0,
            IsActive: true,
            new PrintTemplateLayout(
                Title: kind.ToString(),
                ShowLogo: false,
                LogoPath: null,
                HeaderLines: [],
                FooterNote: null,
                TermsAndConditions: null,
                VisibleColumns: ["Sr", "Item", "Qty", "Rate", "Amount"],
                CopyLabels: []),
            Revisions: []);

    private bool RequireInvoice(out string? problem)
    {
        problem = null;

        if (SelectedDocument is null)
        {
            problem = "Pick an invoice in the list first.";
            return false;
        }

        if (!TradeDocument.IsStockMoving(SelectedDocument.Kind))
        {
            problem = $"{SelectedDocument.Kind} never posts to the books; only invoices do.";
            return false;
        }

        return true;
    }

    private async Task LoadDocumentsAsync(CancellationToken cancellationToken)
    {
        var recent = await _pipeline.ListAsync(TradeDocumentFilter.All, cancellationToken)
            .ConfigureAwait(true);

        Documents = new ObservableCollection<TradeDocumentSummary>(recent);
    }

    /// <summary>
    /// The credit-terms gate on new orders and invoices (spec §2): Block policy refuses, Warn
    /// policy asks. Customers without terms and non-sale documents pass untouched.
    /// </summary>
    private async Task<bool> ConfirmCreditAsync(
        SaveTradeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TradeDocument.IsSalesSide(request.Kind)
            || request.Kind is TradeDocumentKind.Quotation or TradeDocumentKind.DeliveryChallan
            || !_customersByLedgerId.TryGetValue(request.PartyLedgerId, out var customer))
        {
            return true;
        }

        var totalNet = request.Lines.Sum(line =>
            line.Quantity * line.Rate * (1m - line.DiscountPercent / 100m));
        var totalMinor = MoneyScale.ToMinor(MoneyScale.Round(totalNet));

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(true);
        var result = await _creditLimits.EvaluateAsync(companyId, customer.Id, totalMinor, cancellationToken)
            .ConfigureAwait(true);

        if (result.IsBlocked)
        {
            Status = result.Message;
            return false;
        }

        if (result.Policy == CreditLimitPolicy.Warn
            && result.LimitMinor > 0
            && result.CurrentOutstandingMinor + totalMinor > result.LimitMinor
            && MessageBox.Show(
                result.Message + Environment.NewLine + Environment.NewLine + "Continue anyway?",
                "Credit limit warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            Status = "Not saved — the credit-limit warning was not confirmed.";
            return false;
        }

        return true;
    }

    private bool TryBuildRequest(out SaveTradeDocumentRequest? request, out string? problem)
    {
        request = null;
        problem = null;

        if (!IsEditingEnabled)
        {
            problem = "This document is read-only; start a new document instead.";
            return false;
        }

        if (Lines.Count == 0)
        {
            problem = "Add at least one line.";
            return false;
        }

        if (PartyLedger is null)
        {
            problem = NewDocumentKind is TradeDocumentKind.Quotation
                or TradeDocumentKind.SalesOrder
                or TradeDocumentKind.DeliveryChallan
                or TradeDocumentKind.TaxInvoice
                ? "Pick the customer ledger this document bills."
                : "Pick the supplier ledger this document charges.";
            return false;
        }

        var lines = new List<TradeDocumentLineInput>();

        foreach (var editor in Lines)
        {
            if (!decimal.TryParse(editor.Quantity, out var quantity))
            {
                problem = $"The quantity for '{editor.ItemName}' must be a number.";
                return false;
            }

            if (!decimal.TryParse(editor.Rate, out var rate))
            {
                problem = $"The rate for '{editor.ItemName}' must be a number.";
                return false;
            }

            if (!decimal.TryParse(editor.DiscountPercent, out var discountPercent))
            {
                problem = $"The discount for '{editor.ItemName}' must be a number.";
                return false;
            }

            lines.Add(new TradeDocumentLineInput(
                editor.ItemId,
                editor.Godown?.Id,
                quantity,
                rate,
                discountPercent,
                BatchNumber: string.IsNullOrWhiteSpace(editor.BatchNumber) ? null : editor.BatchNumber.Trim(),
                SerialNumbers: editor.ParsedSerials.Count > 0 ? editor.ParsedSerials : null,
                AppliedRateMinor: editor.AppliedRateMinor,
                IsPriceOverridden: editor.IsPriceOverridden,
                PriceSource: editor.PriceSource));
        }

        request = new SaveTradeDocumentRequest(
            NewDocumentKind,
            DateOnly.FromDateTime(DocumentDate),
            PartyLedger.Id,
            lines,
            Narration,
            PartyReferenceNumber: string.IsNullOrWhiteSpace(PartyReferenceNumber)
                ? null
                : PartyReferenceNumber.Trim(),
            PartyReferenceDate: PartyReferenceDate.HasValue
                ? DateOnly.FromDateTime(PartyReferenceDate.Value)
                : null);

        return true;
    }
}
