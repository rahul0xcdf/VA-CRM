using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Voucher entry: one screen parameterised by voucher type, covering posting, alteration,
/// cancellation and history (spec §2).
/// </summary>
/// <remarks>
/// The Dr/Cr grid carries a running difference, and Save stays disabled until that difference reads
/// exactly zero — the same rule the journal engine enforces again behind the UI, so a disabled
/// button and a refusal always agree. Alteration loads the current revision into the same grid and
/// writes a new one; the previous revision stays readable through the history pane, which is the
/// edit log grouped under the voucher.
/// </remarks>
public sealed partial class VoucherEntryViewModel : ObservableObject, IScreenShortcuts
{
    ICommand? IScreenShortcuts.AcceptCommand => PostCommand;

    ICommand? IScreenShortcuts.CancelCommand => CancelVoucherCommand;

    string? IScreenShortcuts.StatusBarHints => "Ctrl+A Post · Ctrl+Q Cancel voucher · Esc Back";

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

    public VoucherEntryViewModel(
        IVoucherPostingService postingService,
        IChartOfAccountsService chartOfAccounts,
        IAuditLogReader auditLogReader)
    {
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
        _auditLogReader = auditLogReader ?? throw new ArgumentNullException(nameof(auditLogReader));
    }

    /// <summary>The difference must read zero before Save enables (spec §2).</summary>
    public bool CanPost =>
        SelectedVoucherType is not null
        && Difference == 0m
        && Lines.Count >= 2
        && Lines.All(line => line.IsValid);

    [RelayCommand]
    private async Task LoadListsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var types = await _postingService.ListVoucherTypesAsync(cancellationToken).ConfigureAwait(true);
            var ledgers = await _chartOfAccounts.ListLedgersAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            VoucherTypes = new ObservableCollection<VoucherTypeSummary>(types);
            Ledgers = new ObservableCollection<LedgerSummary>(
                ledgers.Where(ledger => ledger.IsActive && ledger.AllowsManualPosting));

            if (SelectedVoucherType is null && VoucherTypes.Count > 0)
            {
                SelectedVoucherType = VoucherTypes[0];
            }

            ResetLines();
            Status = $"{VoucherTypes.Count} voucher types, {Ledgers.Count} postable ledgers loaded.";
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
                    LedgerId = line.LedgerId,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    Narration = line.Narration,
                }));

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

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new VoucherLineRow(LineChanged));
        RecomputeTotals();
    }

    [RelayCommand]
    private void RemoveLine(VoucherLineRow? line)
    {
        if (line is null)
        {
            return;
        }

        line.Changed -= LineChanged;
        Lines.Remove(line);
        RecomputeTotals();
    }

    partial void OnLinesChanged(ObservableCollection<VoucherLineRow> value) => WireUp(value);

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

    partial void OnSelectedVoucherTypeChanged(VoucherTypeSummary? value) => PostCommand.NotifyCanExecuteChanged();

    private void ResetLines()
    {
        foreach (var row in Lines)
        {
            row.Changed -= LineChanged;
        }

        Lines = [new VoucherLineRow(LineChanged), new VoucherLineRow(LineChanged)];
        RecomputeTotals();
    }

    private List<PostVoucherLine> CollectLines() =>
        Lines
            .Where(line => line.LedgerId != Guid.Empty)
            .Select(line => new PostVoucherLine(
                line.LedgerId,
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
public sealed partial class VoucherLineRow : ObservableObject
{
    /// <summary>Raised whenever any editable value changes, so the parent recomputes totals.</summary>
    public event Action? Changed;

    public VoucherLineRow(Action? onChanged) => Changed += onChanged;

    [ObservableProperty]
    private Guid _ledgerId;

    [ObservableProperty]
    private decimal _debit;

    [ObservableProperty]
    private decimal _credit;

    [ObservableProperty]
    private string? _narration;

    /// <summary>The shape the journal engine will accept: one named ledger, exactly one side.</summary>
    public bool IsValid =>
        LedgerId != Guid.Empty
        && (Debit > 0m ^ Credit > 0m);

    partial void OnLedgerIdChanged(Guid value) => Changed?.Invoke();

    partial void OnDebitChanged(decimal value) => Changed?.Invoke();

    partial void OnCreditChanged(decimal value) => Changed?.Invoke();
}
