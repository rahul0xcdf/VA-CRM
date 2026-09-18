using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Voucher entry: one screen parameterised by voucher type, covering posting, alteration,
/// cancellation and history (spec §2), reworked to Tally's entry pattern (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// The Dr/Cr grid carries a running difference, and Save stays disabled until that difference reads
/// exactly zero — the same rule the journal engine enforces again behind the UI, so a disabled
/// button and a refusal always agree. Alteration loads the current revision into the same grid and
/// writes a new one; the previous revision stays readable through the history pane, which is the
/// edit log grouped under the voucher.
/// </para>
/// <para>
/// <b>What ADR-0007 changed here.</b> The voucher type was a combo box the operator moused to; it is
/// now <c>F4</c>–<c>F9</c>, the keys a Tally operator already has in their fingers, advertised on the
/// button bar with the current one marked. The ledger column was a combo box that matched on first
/// letter; it is now a lookup that filters on every keystroke and offers <c>Alt+C</c> when the ledger
/// turns out not to exist — the moment that discovery is actually made is halfway through a voucher,
/// and sending the operator to the Ledgers screen to fix it means abandoning the voucher.
/// </para>
/// <para>
/// <b>One key changed meaning.</b> <c>Alt+X</c> used to cancel the posted voucher. In Tally
/// <c>Alt+X</c> abandons what is being typed and <c>Alt+D</c> deletes the record, and those are very
/// different acts to have behind the reflex key — cancelling a posted voucher moves figures out of
/// every statement. <c>Alt+X</c> now clears the form; <c>Alt+D</c> cancels the voucher.
/// </para>
/// </remarks>
public sealed partial class VoucherEntryViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IVoucherPostingService _postingService;
    private readonly IChartOfAccountsService _chartOfAccounts;
    private readonly IAuditLogReader _auditLogReader;

    [ObservableProperty]
    private ObservableCollection<VoucherTypeSummary> _voucherTypes = [];

    [ObservableProperty]
    private VoucherTypeSummary? _selectedVoucherType;

    [ObservableProperty]
    private ObservableCollection<LedgerSummary> _ledgers = [];

    [ObservableProperty]
    private ObservableCollection<VoucherLineRow> _lines = [];

    /// <summary>The line the caret is in, pushed here by the entry grid so Ctrl+D has a target.</summary>
    [ObservableProperty]
    private VoucherLineRow? _currentLine;

    [ObservableProperty]
    private DateTime _entryDate = DateTime.Today;

    [ObservableProperty]
    private string? _narration;

    [ObservableProperty]
    private string? _referenceNumber;

    [ObservableProperty]
    private DateTime? _referenceDate;

    [ObservableProperty]
    private decimal _totalDebit;

    [ObservableProperty]
    private decimal _totalCredit;

    [ObservableProperty]
    private decimal _difference;

    [ObservableProperty]
    private string _alterVoucherIdText = string.Empty;

    [ObservableProperty]
    private string _changeReason = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AuditLogRow> _historyRows = [];

    [ObservableProperty]
    private string _status = "Choose a voucher type, then press Load lists.";

    [ObservableProperty]
    private bool _isBusy;

    // --- Create-a-ledger-without-leaving-the-voucher (Alt+C) ---

    [ObservableProperty]
    private bool _isCreatingLedger;

    [ObservableProperty]
    private string _newLedgerName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AccountGroupSummary> _accountGroups = [];

    [ObservableProperty]
    private AccountGroupSummary? _newLedgerGroup;

    /// <summary>The line whose lookup asked for the new ledger, so it can receive it.</summary>
    private VoucherLineRow? _ledgerCreationTarget;

    public VoucherEntryViewModel(
        IVoucherPostingService postingService,
        IChartOfAccountsService chartOfAccounts,
        IAuditLogReader auditLogReader)
    {
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
        _auditLogReader = auditLogReader ?? throw new ArgumentNullException(nameof(auditLogReader));

        ResetLines();
    }

    // --- The shell's chrome (ADR-0006 contract, ADR-0007 content) ---

    /// <summary>
    /// Ctrl+A. While the create-a-ledger band is open it accepts <em>that</em>, which is how Tally's
    /// sub-screens behave: the innermost thing being edited owns Accept, or an operator finishing a
    /// ledger would post a half-typed voucher instead.
    /// </summary>
    ICommand? IScreenShortcuts.AcceptCommand =>
        IsCreatingLedger ? ConfirmCreateLedgerCommand : PostCommand;

    /// <summary>Alt+X — abandon what is being typed, not what has been posted.</summary>
    ICommand? IScreenShortcuts.CancelCommand =>
        IsCreatingLedger ? CancelCreateLedgerCommand : ClearEntryCommand;

    /// <summary>
    /// One key per voucher type that exists in this company's books.
    /// </summary>
    /// <remarks>
    /// Built from the loaded types rather than from the enum, so a key is only ever shown for a
    /// voucher type that can actually be posted. ADR-0006's rule — a bar never advertises a key that
    /// does nothing — applies as much to a key whose voucher type was deactivated as to one no phase
    /// has implemented yet.
    /// </remarks>
    IReadOnlyList<ShortcutButton> IScreenShortcuts.ButtonBar =>
    [
        .. VoucherTypeKeyMap.KeyedVoucherKinds
            .Select(kind => (Kind: kind, Type: FirstTypeOf(kind)))
            .Where(pair => pair.Type is not null)
            .Select(pair => new ShortcutButton(
                VoucherTypeKeyMap.For(pair.Kind)!,
                pair.Type!.Name,
                SelectVoucherKindCommand,
                pair.Kind,
                IsCurrent: SelectedVoucherType?.Id == pair.Type.Id)),
    ];

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "Create ledger", BeginCreateLedgerCommand),
        new ShortcutButton(AppShortcuts.RemoveLine, "Remove line", RemoveLineCommand, CurrentLine),
        new ShortcutButton(AppShortcuts.Find, "Load for alter", LoadForAlterCommand),
        new ShortcutButton(AppShortcuts.Delete, "Cancel voucher", CancelVoucherCommand),
    ];

    string? IScreenShortcuts.StatusBarHints => IsCreatingLedger
        ? "Ctrl+A Save ledger · Alt+X Abandon it"
        : "F4-F9 Voucher type · Alt+C Create ledger · Ctrl+A Post · Alt+D Cancel voucher";

    /// <summary>The difference must read zero before Save enables (spec §2).</summary>
    public bool CanPost =>
        SelectedVoucherType is not null
        && Difference == 0m
        && Lines.Count(line => line.IsValid) >= 2
        && Lines.All(line => line.IsBlank || line.IsValid);

    /// <summary>True once a ledger and a group are named, so the create band can be accepted.</summary>
    public bool CanConfirmCreateLedger =>
        IsCreatingLedger
        && !string.IsNullOrWhiteSpace(NewLedgerName)
        && NewLedgerGroup is not null;

    [RelayCommand]
    private async Task LoadListsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var types = await _postingService.ListVoucherTypesAsync(cancellationToken).ConfigureAwait(true);
            var ledgers = await _chartOfAccounts.ListLedgersAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            var groups = await _chartOfAccounts.ListGroupsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            VoucherTypes = new ObservableCollection<VoucherTypeSummary>(types);
            Ledgers = new ObservableCollection<LedgerSummary>(
                ledgers.Where(ledger => ledger.IsActive && ledger.AllowsManualPosting));
            AccountGroups = new ObservableCollection<AccountGroupSummary>(
                groups.Where(group => group.IsActive));

            if (SelectedVoucherType is null && VoucherTypes.Count > 0)
            {
                SelectedVoucherType = VoucherTypes[0];
            }

            ResetLines();
            Status = $"{VoucherTypes.Count} voucher types, {Ledgers.Count} postable ledgers loaded. " +
                     "F4-F9 switches voucher type.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyBarsChanged();
        }
    }

    /// <summary>
    /// F4-F9: switch the voucher being entered, exactly as Tally does.
    /// </summary>
    /// <remarks>
    /// The lines are deliberately <em>not</em> cleared. An operator who reaches for F6 having started
    /// on F5 has mistyped the voucher type, not the figures, and throwing away two typed lines to
    /// punish a keystroke is the kind of thing that teaches people to avoid the keyboard.
    /// </remarks>
    [RelayCommand]
    private void SelectVoucherKind(VoucherKind kind)
    {
        var type = FirstTypeOf(kind);

        if (type is null)
        {
            Status = $"No active {kind} voucher type exists in this company.";
            return;
        }

        SelectedVoucherType = type;
        Status = $"{type.Name}. Lines are kept — only the voucher type changed.";
    }

    private VoucherTypeSummary? FirstTypeOf(VoucherKind kind) =>
        VoucherTypes.FirstOrDefault(type => type.Kind == kind && type.IsActive);

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var posted = await _postingService.PostAsync(new PostVoucherRequest(
                SelectedVoucherType!.Id,
                DateOnly.FromDateTime(EntryDate),
                CollectLines(),
                Narration,
                ReferenceNumber,
                ToBusinessDate(ReferenceDate)),
                cancellationToken)
                .ConfigureAwait(true);

            Status = $"Posted {posted.Number} (revision {posted.RevisionNumber}).";
            AlterVoucherIdText = posted.VoucherId.ToString();
            ChangeReason = string.Empty;

            ResetLines();
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

    /// <summary>Alt+X — empty the form and start again, without touching anything posted.</summary>
    [RelayCommand]
    private void ClearEntry()
    {
        Narration = null;
        ReferenceNumber = null;
        ReferenceDate = null;
        ChangeReason = string.Empty;
        ResetLines();

        Status = "Entry cleared. Nothing posted was touched.";
    }

    [RelayCommand]
    private async Task LoadForAlterAsync(CancellationToken cancellationToken)
    {
        if (!TryParseVoucherId(out var voucherId))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var current = await _postingService.GetCurrentAsync(voucherId, cancellationToken)
                .ConfigureAwait(true);

            if (current is null)
            {
                Status = $"No voucher '{AlterVoucherIdText.Trim()}' exists.";
                return;
            }

            if (current.Status == VoucherStatus.Cancelled)
            {
                Status = $"Voucher {current.Number} is cancelled; its history is closed.";
            }

            SelectedVoucherType = VoucherTypes.FirstOrDefault(type => type.Id == current.VoucherTypeId)
                ?? SelectedVoucherType;

            EntryDate = current.VoucherDate.ToDateTime(TimeOnly.MinValue);
            Narration = current.Narration;
            ReferenceNumber = null;
            ReferenceDate = null;

            Lines = new ObservableCollection<VoucherLineRow>(current.Lines.Select(line =>
                new VoucherLineRow(LineChanged)
                {
                    Ledger = FindLedger(line.LedgerId),
                    Debit = line.Debit,
                    Credit = line.Credit,
                    Narration = line.Narration,
                }));

            RecomputeTotals();

            Status = $"Loaded {current.Number} (revision {current.RevisionNumber}, {current.Status}). " +
                     "Enter a change reason, then press Apply alteration.";
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
    private async Task ApplyAlterationAsync(CancellationToken cancellationToken)
    {
        if (!TryParseVoucherId(out var voucherId))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var altered = await _postingService.AlterAsync(new AlterVoucherRequest(
                voucherId,
                DateOnly.FromDateTime(EntryDate),
                CollectLines(),
                ChangeReason,
                Narration,
                ReferenceNumber,
                ToBusinessDate(ReferenceDate)),
                cancellationToken)
                .ConfigureAwait(true);

            Status = $"Altered: {altered.Number} now at revision {altered.RevisionNumber}. " +
                     "The previous revision remains in history.";
            ChangeReason = string.Empty;
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
    private async Task CancelVoucherAsync(CancellationToken cancellationToken)
    {
        if (!TryParseVoucherId(out var voucherId))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var cancelled = await _postingService.CancelAsync(voucherId, ChangeReason, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Cancelled {cancelled.Number}. Its number stays allocated and its figures " +
                     "leave every statement.";
            ChangeReason = string.Empty;
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
    private async Task ViewHistoryAsync(CancellationToken cancellationToken)
    {
        if (!TryParseVoucherId(out var voucherId))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var rows = await _auditLogReader.ListForEntityAsync(
                nameof(Voucher),
                voucherId.ToString("D"),
                cancellationToken)
                .ConfigureAwait(true);

            // Newest first from the query; shown oldest first so history reads like a story.
            HistoryRows = new ObservableCollection<AuditLogRow>(rows.Reverse());

            Status = $"{HistoryRows.Count} edit-log rows for this voucher.";
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

    // --- Alt+C: create the ledger from inside the voucher ---

    /// <summary>
    /// Opens the create band, pre-named with whatever was typed into the lookup.
    /// </summary>
    /// <remarks>
    /// A band at the foot of the screen rather than a modal dialog, per the Pages.xaml rule. The
    /// half-typed voucher stays on screen and visibly intact, which is the whole point: an operator
    /// who cannot see their work while a dialog sits over it does not trust that it survived.
    /// </remarks>
    [RelayCommand]
    private void BeginCreateLedger(object? typedName)
    {
        _ledgerCreationTarget = CurrentLine;

        NewLedgerName = typedName as string ?? string.Empty;
        NewLedgerGroup = null;
        IsCreatingLedger = true;

        Status = AccountGroups.Count == 0
            ? "Press Load lists first — the group list has not been loaded."
            : "New ledger: name it, choose its group, then Ctrl+A.";
    }

    [RelayCommand(CanExecute = nameof(CanConfirmCreateLedger))]
    private async Task ConfirmCreateLedgerAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var created = await _chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest(NewLedgerName.Trim(), NewLedgerGroup!.Id),
                cancellationToken)
                .ConfigureAwait(true);

            Ledgers.Add(created);

            // Straight onto the line the operator was filling, so Alt+C costs nothing but the name.
            if (_ledgerCreationTarget is not null && created.AllowsManualPosting)
            {
                _ledgerCreationTarget.Ledger = created;
            }

            Status = created.AllowsManualPosting
                ? $"Created '{created.Name}' under {created.AccountGroupName}."
                : $"Created '{created.Name}', but its group does not allow manual posting.";

            CloseCreateLedger();
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
    private void CancelCreateLedger()
    {
        CloseCreateLedger();
        Status = "Ledger abandoned. The voucher is untouched.";
    }

    private void CloseCreateLedger()
    {
        IsCreatingLedger = false;
        NewLedgerName = string.Empty;
        NewLedgerGroup = null;
        _ledgerCreationTarget = null;
    }

    // --- Lines ---

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new VoucherLineRow(LineChanged));
        RecomputeTotals();
    }

    /// <summary>
    /// Ctrl+D. Falls back to the line the caret is in when the shell invokes it from the action bar
    /// without a parameter, which is how a key pressed anywhere on the screen still knows its target.
    /// </summary>
    [RelayCommand]
    private void RemoveLine(VoucherLineRow? line)
    {
        var target = line ?? CurrentLine;

        if (target is null)
        {
            Status = "Put the caret on the line to remove first.";
            return;
        }

        target.Changed -= LineChanged;
        Lines.Remove(target);

        if (ReferenceEquals(CurrentLine, target))
        {
            CurrentLine = null;
        }

        // A voucher always has somewhere to type; removing the last line would leave a dead screen.
        if (Lines.Count == 0)
        {
            Lines.Add(new VoucherLineRow(LineChanged));
        }

        RecomputeTotals();
    }

    partial void OnLinesChanged(ObservableCollection<VoucherLineRow> value) => WireUp(value);

    partial void OnCurrentLineChanged(VoucherLineRow? value) => NotifyBarsChanged();

    partial void OnIsCreatingLedgerChanged(bool value)
    {
        ConfirmCreateLedgerCommand.NotifyCanExecuteChanged();
        NotifyBarsChanged();
    }

    partial void OnNewLedgerNameChanged(string value) => ConfirmCreateLedgerCommand.NotifyCanExecuteChanged();

    partial void OnNewLedgerGroupChanged(AccountGroupSummary? value) =>
        ConfirmCreateLedgerCommand.NotifyCanExecuteChanged();

    partial void OnVoucherTypesChanged(ObservableCollection<VoucherTypeSummary> value) => NotifyBarsChanged();

    private void WireUp(IEnumerable<VoucherLineRow> rows)
    {
        foreach (var row in rows)
        {
            row.Changed -= LineChanged;
            row.Changed += LineChanged;
        }
    }

    private void LineChanged()
    {
        RecomputeTotals();
    }

    private void RecomputeTotals()
    {
        TotalDebit = Lines.Sum(line => line.Debit);
        TotalCredit = Lines.Sum(line => line.Credit);
        Difference = TotalDebit - TotalCredit;

        PostCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVoucherTypeChanged(VoucherTypeSummary? value)
    {
        PostCommand.NotifyCanExecuteChanged();
        NotifyBarsChanged();
    }

    /// <summary>
    /// Tells the shell to re-read both bars.
    /// </summary>
    /// <remarks>
    /// The shell subscribes to <c>PropertyChanged</c> for these names; because it also routes keys
    /// from the bars, a bar left stale would leave a key firing the wrong command rather than merely
    /// looking wrong.
    /// </remarks>
    private void NotifyBarsChanged()
    {
        OnPropertyChanged(nameof(IScreenShortcuts.ButtonBar));
        OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));
        OnPropertyChanged(nameof(IScreenShortcuts.StatusBarHints));
    }

    private void ResetLines()
    {
        foreach (var row in Lines)
        {
            row.Changed -= LineChanged;
        }

        CurrentLine = null;
        Lines = [new VoucherLineRow(LineChanged), new VoucherLineRow(LineChanged)];
        RecomputeTotals();
    }

    private LedgerSummary? FindLedger(Guid id) => Ledgers.FirstOrDefault(ledger => ledger.Id == id);

    private List<PostVoucherLine> CollectLines() =>
        Lines
            .Where(line => line.Ledger is not null)
            .Select(line => new PostVoucherLine(
                line.Ledger!.Id,
                line.Debit,
                line.Credit,
                line.Narration))
            .ToList();

    private bool TryParseVoucherId(out Guid voucherId)
    {
        if (!Guid.TryParse(AlterVoucherIdText.Trim(), out voucherId) || voucherId == Guid.Empty)
        {
            Status = "Paste the voucher id (shown after posting, or from the day book) first.";
            return false;
        }

        return true;
    }

    private static DateOnly? ToBusinessDate(DateTime? value) =>
        value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
}

/// <summary>One editable line of the voucher grid.</summary>
/// <remarks>
/// The line carries the <see cref="LedgerSummary"/> itself rather than its id (ADR-0007). A lookup
/// field selects an object, and holding the id here would mean converting back to an object on every
/// render just to show a name the row already had.
/// </remarks>
public sealed partial class VoucherLineRow : ObservableObject
{
    /// <summary>Raised whenever any editable value changes, so the parent recomputes totals.</summary>
    public event Action? Changed;

    public VoucherLineRow(Action? onChanged) => Changed += onChanged;

    [ObservableProperty]
    private LedgerSummary? _ledger;

    [ObservableProperty]
    private decimal _debit;

    [ObservableProperty]
    private decimal _credit;

    [ObservableProperty]
    private string? _narration;

    /// <summary>The shape the journal engine will accept: one named ledger, exactly one side.</summary>
    public bool IsValid =>
        Ledger is not null
        && (Debit > 0m ^ Credit > 0m);

    /// <summary>
    /// Nothing typed yet.
    /// </summary>
    /// <remarks>
    /// A voucher grid always shows one empty line past the last filled one, so an operator can keep
    /// typing. Counting that line as invalid would keep Post disabled on every well-formed voucher.
    /// </remarks>
    public bool IsBlank =>
        Ledger is null && Debit == 0m && Credit == 0m && string.IsNullOrWhiteSpace(Narration);

    partial void OnLedgerChanged(LedgerSummary? value) => Changed?.Invoke();

    partial void OnDebitChanged(decimal value) => Changed?.Invoke();

    partial void OnCreditChanged(decimal value) => Changed?.Invoke();
}
