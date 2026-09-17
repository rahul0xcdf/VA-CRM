using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The e-invoice screen (phase-6-einvoice-eway.md §2): register posted tax invoices with the IRP,
/// see their IRN and acknowledgement, and cancel within the 24-hour window.
/// </summary>
/// <remarks>
/// <para>
/// The left side lists posted tax invoices and whether each one has an IRN; the right side shows the
/// selected invoice's registration in full. "Preview payload" exists because, until NIC credentials
/// arrive (<c>open-questions.md</c> #10), it is the only way to see what would be sent — and after
/// they arrive it stays the fastest way to find out why the portal refused something.
/// </para>
/// <para>
/// Cancel is disabled rather than hidden once the window closes, with the deadline shown. Hiding it
/// would leave the user wondering where it went; showing it greyed out with a date answers the
/// question before it is asked.
/// </para>
/// </remarks>
public sealed partial class EinvoiceRegistrationsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Get IRN · Ctrl+Q Clear · Alt+G Go To";

    private readonly IEinvoiceService _einvoices;
    private readonly ITradeDocumentService _documents;

    [ObservableProperty]
    private ObservableCollection<EinvoiceInvoiceRow> _invoices = [];

    [ObservableProperty]
    private EinvoiceInvoiceRow? _selectedInvoice;

    [ObservableProperty]
    private string _irn = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _acknowledgement = string.Empty;

    [ObservableProperty]
    private string _signedQrCode = string.Empty;

    [ObservableProperty]
    private string _cancellableUntil = string.Empty;

    [ObservableProperty]
    private string _payloadPreview = string.Empty;

    [ObservableProperty]
    private string _cancelReasonCode = "1";

    [ObservableProperty]
    private string _cancelRemarks = string.Empty;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public EinvoiceRegistrationsViewModel(
        IEinvoiceService einvoices,
        ITradeDocumentService documents)
    {
        _einvoices = einvoices ?? throw new ArgumentNullException(nameof(einvoices));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => GenerateCommand;

    public ICommand? CancelCommand => ClearCommand;

    partial void OnSelectedInvoiceChanged(EinvoiceInvoiceRow? value)
    {
        ClearDetail();

        if (value is not null)
        {
            _ = LoadDetailAsync(value.DocumentId, CancellationToken.None);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var documents = await _documents
                .ListAsync(
                    new TradeDocumentFilter { Kinds = [TradeDocumentKind.TaxInvoice], Take = 200 },
                    cancellationToken)
                .ConfigureAwait(true);

            var registrations = await _einvoices.ListAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            var byDocument = registrations.ToDictionary(
                registration => registration.TradeDocumentId);

            var rows = documents
                // Only a posted invoice can be registered: the IRN is computed from its number, and a
                // draft has none.
                .Where(document => document.PostedVoucherId is not null
                    && document.Status is not TradeDocumentStatus.Cancelled)
                .OrderByDescending(document => document.DocumentDate)
                .ThenByDescending(document => document.Number, StringComparer.Ordinal)
                .Select(document => new EinvoiceInvoiceRow(
                    document.Id,
                    document.DisplayNumber,
                    document.DocumentDate,
                    document.PartyLedgerName,
                    document.TotalNetValue,
                    byDocument.TryGetValue(document.Id, out var registration)
                        ? registration.Status.ToString()
                        : "Not registered",
                    byDocument.TryGetValue(document.Id, out var found) ? found.Irn : string.Empty))
                .ToList();

            Invoices = [.. rows];

            var registered = rows.Count(row => row.Irn.Length > 0);
            Status = $"{rows.Count} posted invoice(s); {registered} registered.";
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
    /// Asks the portal for an IRN. Idempotent on the service side, so pressing this twice is safe —
    /// the second press reports the existing registration rather than making a second one.
    /// </summary>
    [RelayCommand]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice)
        {
            Status = "Pick a posted tax invoice first.";
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _einvoices.GenerateAsync(invoice.DocumentId, cancellationToken)
                .ConfigureAwait(true);

            Apply(result.Registration);

            Status = result.WasAlreadyRegistered
                ? $"Already registered. IRN {result.Registration.Irn}."
                : result.Registration.Status is EinvoiceStatus.Generated
                    ? $"Registered. Acknowledgement {result.Registration.AcknowledgementNumber}."
                    : $"Not registered: {result.Registration.LastErrorText}";

            // A portal IRN that differs from ours means one of the four hash inputs is not what we
            // think it is — worth saying loudly rather than leaving in a log (checkpoint C6.4).
            if (result.PortalIrnMatchedOurs is false)
            {
                Status += " The portal's IRN did not match the one computed here.";
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task PreviewPayloadAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice)
        {
            Status = "Pick a posted tax invoice first.";
            return;
        }

        IsBusy = true;

        try
        {
            var request = await _einvoices.BuildRequestAsync(invoice.DocumentId, cancellationToken)
                .ConfigureAwait(true);

            var qr = EinvoiceQrBuilder.Build(request, new string('0', 64), "pending");

            PayloadPreview = string.Join(
                Environment.NewLine,
                $"Document      {request.DocumentNumber} ({EinvoiceDocumentTypeCodes.Code(request.DocumentType)})",
                $"Date          {request.DocumentDate:dd/MM/yyyy}",
                $"Seller        {request.Seller.Gstin}  {request.Seller.LegalName}",
                $"Buyer         {request.Buyer.Gstin}  {request.Buyer.LegalName}",
                $"Place of supply {request.PlaceOfSupplyStateCode}",
                $"Reverse charge  {(request.IsReverseCharge ? "Yes" : "No")}",
                $"Lines         {request.Lines.Count}",
                $"Main HSN      {qr.MainHsnCode}",
                $"Invoice value {Money(qr.TotalInvoiceValueMinor)}",
                string.Empty,
                "IRN hash input:",
                IrnFormula.HashInput(
                    request.Seller.Gstin,
                    request.DocumentDate,
                    request.DocumentType,
                    request.DocumentNumber),
                string.Empty,
                "Computed IRN:",
                IrnFormula.Compute(
                    request.Seller.Gstin,
                    request.DocumentDate,
                    request.DocumentType,
                    request.DocumentNumber));

            Status = "Payload assembled. Nothing has been sent to the portal.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            PayloadPreview = string.Empty;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelIrnAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice || Irn.Length is 0)
        {
            Status = "Pick a registered invoice first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CancelReasonCode))
        {
            Status = "The portal requires a cancellation reason code.";
            return;
        }

        IsBusy = true;

        try
        {
            var registration = await _einvoices.FindByDocumentAsync(invoice.DocumentId, cancellationToken)
                .ConfigureAwait(true);

            if (registration is null)
            {
                Status = "This invoice has no registration to cancel.";
                return;
            }

            var cancelled = await _einvoices
                .CancelAsync(registration.Id, CancelReasonCode.Trim(), NullIfEmpty(CancelRemarks), cancellationToken)
                .ConfigureAwait(true);

            Apply(cancelled);
            Status = $"Cancelled at the portal. IRN {cancelled.Irn} is no longer valid.";

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
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
    private void Clear()
    {
        SelectedInvoice = null;
        ClearDetail();
        PayloadPreview = string.Empty;
        Status = "Cleared.";
    }

    private async Task LoadDetailAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var registration = await _einvoices.FindByDocumentAsync(documentId, cancellationToken)
                .ConfigureAwait(true);

            if (registration is not null)
            {
                Apply(registration);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
    }

    private void Apply(EinvoiceRegistrationSummary registration)
    {
        Irn = registration.Irn;
        StatusText = registration.Status.ToString();
        Acknowledgement = registration.AcknowledgementNumber is { } number
            ? $"{number} on {registration.PortalAckDateText}"
            : "—";
        SignedQrCode = registration.SignedQrCodeJws ?? string.Empty;
        CancellableUntil = registration.CancellableUntilUtc is { } until
            ? until.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture)
            : "—";
        CanCancel = registration.Status is EinvoiceStatus.Generated
            && registration.CancellableUntilUtc > DateTimeOffset.UtcNow;
    }

    private void ClearDetail()
    {
        Irn = string.Empty;
        StatusText = string.Empty;
        Acknowledgement = string.Empty;
        SignedQrCode = string.Empty;
        CancellableUntil = string.Empty;
        CanCancel = false;
    }

    private static string Money(long minor) =>
        Domain.Common.MoneyScale.FromMinor(minor).ToString("N2", CultureInfo.CurrentCulture);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One posted tax invoice and whether it carries an IRN.</summary>
public sealed record EinvoiceInvoiceRow(
    Guid DocumentId,
    string Number,
    DateOnly DocumentDate,
    string PartyName,
    decimal TotalNetValue,
    string RegistrationStatus,
    string Irn);
