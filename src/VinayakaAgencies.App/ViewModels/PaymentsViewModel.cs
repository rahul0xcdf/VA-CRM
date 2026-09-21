using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Domain.Banking;
using VinayakaAgencies.Domain.Printing;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Payments and receipts with the instrument they settled through, and the cheque register
/// (phase-7-tds-payroll-banking.md §2, checkpoint C7.8).
/// </summary>
/// <remarks>
/// <para>
/// Entry screen per ADR-0007: every master-picking field is a <c>LookupBox</c>, the mode governs
/// which identifier fields mean anything, and the rule that decides which lives in
/// <c>PaymentInstrument</c> where a unit test can reach it without a window.
/// </para>
/// <para>
/// <c>Ctrl+P</c> prints the selected cheque through whichever cheque layout is default — the same
/// print-template machinery Phase 3 uses, per §4a D4. There is no second template system to learn.
/// </para>
/// </remarks>
public sealed partial class PaymentsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints =
        "Ctrl+A Post · Ctrl+P Print cheque · Alt+X Clear · F5 Payment · F6 Receipt";

    private readonly IBankingService _banking;
    private readonly IVoucherPostingService _vouchers;
    private readonly IChartOfAccountsService _chart;
    private readonly IPrintTemplateService _templates;

    [ObservableProperty]
    private ObservableCollection<PaymentInstrumentSummary> _rows = [];

    [ObservableProperty]
    private ObservableCollection<LedgerChoice> _ledgerChoices = [];

    [ObservableProperty]
    private PaymentInstrumentSummary? _selectedRow;

    [ObservableProperty]
    private LedgerChoice? _partyLedger;

    [ObservableProperty]
    private LedgerChoice? _bankLedger;

    [ObservableProperty]
    private PaymentMode _mode = PaymentMode.Cheque;

    [ObservableProperty]
    private bool _isReceipt;

    [ObservableProperty]
    private DateTime _voucherDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _instrumentDate = DateTime.Today;

    [ObservableProperty]
    private string _amount = string.Empty;

    [ObservableProperty]
    private string _instrumentNumber = string.Empty;

    [ObservableProperty]
    private string _bankReference = string.Empty;

    [ObservableProperty]
    private string _payeeName = string.Empty;

    [ObservableProperty]
    private string _narration = string.Empty;

    [ObservableProperty]
    private DateTime _fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private string _amountInWords = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public PaymentsViewModel(
        IBankingService banking,
        IVoucherPostingService vouchers,
        IChartOfAccountsService chart,
        IPrintTemplateService templates)
    {
        _banking = banking ?? throw new ArgumentNullException(nameof(banking));
        _vouchers = vouchers ?? throw new ArgumentNullException(nameof(vouchers));
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => PostCommand;

    public ICommand? CancelCommand => ClearCommand;

    public IReadOnlyList<PaymentMode> ModeChoices { get; } =
    [
        PaymentMode.Cheque,
        PaymentMode.Neft,
        PaymentMode.Rtgs,
        PaymentMode.Imps,
        PaymentMode.Upi,
        PaymentMode.DemandDraft,
        PaymentMode.Card,
        PaymentMode.BankTransfer,
        PaymentMode.Cash,
    ];

    /// <summary>True for the one mode that has an instrument number and a printed layout.</summary>
    public bool NeedsInstrumentNumber =>
        Mode is PaymentMode.Cheque or PaymentMode.DemandDraft;

    /// <summary>True for every rail whose identifier the bank issues and we record.</summary>
    public bool NeedsBankReference =>
        Mode is PaymentMode.Neft or PaymentMode.Rtgs or PaymentMode.Imps
            or PaymentMode.Upi or PaymentMode.Card or PaymentMode.BankTransfer;

    /// <inheritdoc />
    /// <remarks>
    /// F5 and F6 carry TallyPrime's published meanings — payment and receipt — and here they
    /// switch the direction of the entry rather than the voucher type, because this screen only
    /// ever posts those two.
    /// </remarks>
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.Payment, "Payment", SetPaymentCommand),
        new ShortcutButton(AppShortcuts.Receipt, "Receipt", SetReceiptCommand),
        new ShortcutButton(AppShortcuts.Print, "Print cheque", PrintChequeCommand),
    ];

    partial void OnModeChanged(PaymentMode value)
    {
        OnPropertyChanged(nameof(NeedsInstrumentNumber));
        OnPropertyChanged(nameof(NeedsBankReference));
    }

    partial void OnAmountChanged(string value) =>
        AmountInWords = TryParseAmount(value, out var amount) && amount > 0m
            ? IndianAmountInWords.ForCheque(amount)
            : string.Empty;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            if (LedgerChoices.Count == 0)
            {
                var ledgers = await _chart.ListLedgersAsync(includeInactive: false, cancellationToken)
                    .ConfigureAwait(true);

                LedgerChoices = new ObservableCollection<LedgerChoice>(
                    ledgers.Select(ledger => new LedgerChoice(ledger.Id, ledger.Name)));
            }

            var register = await _banking.ListAsync(
                    DateOnly.FromDateTime(FromDate),
                    DateOnly.FromDateTime(ToDate),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            Rows = new ObservableCollection<PaymentInstrumentSummary>(register);
            Status = $"{Rows.Count} settlement(s) in this period.";
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
        if (PartyLedger is null || BankLedger is null)
        {
            Status = "Pick the party and the bank or cash account.";
            return;
        }

        if (!TryParseAmount(Amount, out var amount) || amount <= 0m)
        {
            Status = "Enter the amount.";
            return;
        }

        IsBusy = true;

        try
        {
            var kind = IsReceipt
                ? VinayakaAgencies.Domain.Accounting.VoucherKind.Receipt
                : VinayakaAgencies.Domain.Accounting.VoucherKind.Payment;

            var voucherTypes = await _vouchers.ListVoucherTypesAsync(cancellationToken)
                .ConfigureAwait(true);

            var voucherType = voucherTypes.FirstOrDefault(type => type.Kind == kind && type.IsActive);

            if (voucherType is null)
            {
                Status = $"No active {kind} voucher type is configured.";
                return;
            }

            var posted = await _banking.PostAsync(
                    new PostBankVoucherRequest(
                        voucherType.Id,
                        DateOnly.FromDateTime(VoucherDate),
                        PartyLedger.Id,
                        BankLedger.Id,
                        amount,
                        Mode,
                        DateOnly.FromDateTime(InstrumentDate),
                        NullIfEmpty(InstrumentNumber),
                        NullIfEmpty(BankReference),
                        NullIfEmpty(PayeeName),
                        NullIfEmpty(Narration),
                        Remarks: null,
                        IsReceipt: IsReceipt),
                    cancellationToken)
                .ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Posted {posted.VoucherNumber} — {posted.Mode} {amount:N2} "
                + $"{(IsReceipt ? "from" : "to")} {posted.PartyLedgerName}.";

            Clear();
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
    /// Ctrl+P: prints the selected cheque through the default cheque layout.
    /// </summary>
    /// <remarks>
    /// The layout is resolved the same way an invoice's is — lowest active priority for the kind
    /// (C3.6) — because §4a D4 reuses the Phase 3 engine rather than standing up a second one.
    /// </remarks>
    [RelayCommand]
    private async Task PrintChequeAsync(CancellationToken cancellationToken)
    {
        if (SelectedRow is null)
        {
            Status = "Pick a settlement to print.";
            return;
        }

        if (!SelectedRow.IsPrintable)
        {
            Status = $"A {SelectedRow.Mode} payment has no cheque to print.";
            return;
        }

        IsBusy = true;

        try
        {
            var cheque = await _banking.GetChequeDataAsync(SelectedRow.Id, cancellationToken)
                .ConfigureAwait(true);

            if (cheque is null)
            {
                Status = "There is no cheque on that voucher.";
                return;
            }

            var template = await _templates
                .ResolveDefaultAsync(PrintDocumentKind.Cheque, cancellationToken)
                .ConfigureAwait(true);

            var detail = template is null
                ? null
                : await _templates.GetAsync(template.Id, cancellationToken).ConfigureAwait(true);

            var layout = detail?.Layout ?? ChequeLayoutDefaults.Default;

            new Views.ChequePreviewWindow(cheque, layout).Show();

            await _banking.MarkPrintedAsync(SelectedRow.Id, cancellationToken).ConfigureAwait(true);
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Cheque {cheque.ChequeNumber} sent to preview and marked printed.";
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
    private void SetPayment()
    {
        IsReceipt = false;
        Status = "Payment — the party is debited and the bank credited.";
    }

    [RelayCommand]
    private void SetReceipt()
    {
        IsReceipt = true;
        Status = "Receipt — the bank is debited and the party credited.";
    }

    [RelayCommand]
    private void Clear()
    {
        PartyLedger = null;
        Amount = string.Empty;
        InstrumentNumber = string.Empty;
        BankReference = string.Empty;
        PayeeName = string.Empty;
        Narration = string.Empty;
        AmountInWords = string.Empty;
        VoucherDate = DateTime.Today;
        InstrumentDate = DateTime.Today;
    }

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(
            text?.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>A ledger in a lookup field.</summary>
public sealed record LedgerChoice(Guid Id, string Name);
