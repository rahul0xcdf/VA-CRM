using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The e-way bill screen (phase-6-einvoice-eway.md §2): generate by IRN, keep Part-B current, cancel
/// and extend.
/// </summary>
/// <remarks>
/// <para>
/// Part-B's required fields change with the transport mode, and the screen says which are needed
/// rather than marking everything mandatory: by road it is the vehicle number, and for rail, air or
/// ship it is the carrier's document number and date. The same rule is enforced in the domain, so a
/// screen bypassed by a script cannot produce a half-filled bill.
/// </para>
/// <para>
/// Whether a bill is <em>required</em> is shown, never enforced. The threshold applies to a
/// consignment, and a consignment is not always one invoice — several may travel together — so the
/// software states the position and leaves the decision where it belongs.
/// </para>
/// </remarks>
public sealed partial class EwayBillsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Generate · Alt+X Clear";

    private readonly IEwayBillService _ewayBills;
    private readonly ITradeDocumentService _documents;

    [ObservableProperty]
    private ObservableCollection<EwayBillInvoiceRow> _invoices = [];

    [ObservableProperty]
    private EwayBillInvoiceRow? _selectedInvoice;

    [ObservableProperty]
    private ObservableCollection<string> _transportModes =
        [.. Enum.GetNames<TransportMode>()];

    [ObservableProperty]
    private string _selectedTransportMode = nameof(TransportMode.Road);

    [ObservableProperty]
    private ObservableCollection<string> _vehicleTypes =
        [.. Enum.GetNames<VehicleType>()];

    [ObservableProperty]
    private string _selectedVehicleType = nameof(VehicleType.Regular);

    [ObservableProperty]
    private string _distanceKilometres = "0";

    [ObservableProperty]
    private string _vehicleNumber = string.Empty;

    [ObservableProperty]
    private string _transporterGstin = string.Empty;

    [ObservableProperty]
    private string _transporterName = string.Empty;

    [ObservableProperty]
    private string _transportDocumentNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _transportDocumentDate;

    [ObservableProperty]
    private string _billNumber = string.Empty;

    [ObservableProperty]
    private string _billStatus = string.Empty;

    [ObservableProperty]
    private string _validUntil = string.Empty;

    [ObservableProperty]
    private string _expectedValidity = string.Empty;

    [ObservableProperty]
    private string _requirement = string.Empty;

    [ObservableProperty]
    private string _reasonCode = "1";

    [ObservableProperty]
    private string _remarks = string.Empty;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public EwayBillsViewModel(IEwayBillService ewayBills, ITradeDocumentService documents)
    {
        _ewayBills = ewayBills ?? throw new ArgumentNullException(nameof(ewayBills));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => GenerateCommand;

    public ICommand? CancelCommand => ClearCommand;

    /// <summary>What Part-B needs for the mode currently chosen.</summary>
    public string PartBRequirement =>
        string.Equals(SelectedTransportMode, nameof(TransportMode.Road), StringComparison.Ordinal)
            ? "By road: vehicle number is required."
            : "By rail, air or ship: transport document number and date are required.";

    partial void OnSelectedTransportModeChanged(string value)
    {
        OnPropertyChanged(nameof(PartBRequirement));
        RecomputeExpectedValidity();
    }

    partial void OnSelectedVehicleTypeChanged(string value) => RecomputeExpectedValidity();

    partial void OnDistanceKilometresChanged(string value) => RecomputeExpectedValidity();

    partial void OnSelectedInvoiceChanged(EwayBillInvoiceRow? value)
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
                    new TradeDocumentFilter
                    {
                        Kinds = [TradeDocumentKind.TaxInvoice, TradeDocumentKind.DeliveryChallan],
                        Take = 200,
                    },
                    cancellationToken)
                .ConfigureAwait(true);

            var bills = await _ewayBills.ListAsync(cancellationToken).ConfigureAwait(true);
            var byDocument = bills.ToDictionary(bill => bill.TradeDocumentId);

            Invoices =
            [
                .. documents
                    .Where(document => document.PostedVoucherId is not null
                        && document.Status is not TradeDocumentStatus.Cancelled)
                    .OrderByDescending(document => document.DocumentDate)
                    .Select(document => new EwayBillInvoiceRow(
                        document.Id,
                        document.DisplayNumber,
                        document.DocumentDate,
                        document.PartyLedgerName,
                        document.TotalNetValue,
                        byDocument.TryGetValue(document.Id, out var bill)
                            ? bill.EwayBillNumber?.ToString(CultureInfo.InvariantCulture) ?? bill.Status.ToString()
                            : "—")),
            ];

            Status = $"{Invoices.Count} movable document(s); {bills.Count} e-way bill(s) on file.";
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
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice)
        {
            Status = "Pick a posted invoice or challan first.";
            return;
        }

        if (!TryBuildPartB(out var partB, out var distance, out var problem))
        {
            Status = problem;
            return;
        }

        IsBusy = true;

        try
        {
            Apply(await _ewayBills
                .GenerateAsync(invoice.DocumentId, distance, partB, cancellationToken)
                .ConfigureAwait(true));

            Status = BillNumber.Length > 0
                ? $"E-way bill {BillNumber} issued, valid until {ValidUntil}."
                : $"Not issued: {Status}";

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
    private async Task UpdatePartBAsync(CancellationToken cancellationToken)
    {
        if (await RequireBillAsync(cancellationToken).ConfigureAwait(true) is not { } bill)
        {
            return;
        }

        if (!TryBuildPartB(out var partB, out _, out var problem))
        {
            Status = problem;
            return;
        }

        IsBusy = true;

        try
        {
            Apply(await _ewayBills.UpdatePartBAsync(bill.Id, partB, cancellationToken)
                .ConfigureAwait(true));

            Status = "Part-B updated at the portal.";
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
    private async Task CancelBillAsync(CancellationToken cancellationToken)
    {
        if (await RequireBillAsync(cancellationToken).ConfigureAwait(true) is not { } bill)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Apply(await _ewayBills
                .CancelAsync(bill.Id, ReasonCode.Trim(), NullIfEmpty(Remarks), cancellationToken)
                .ConfigureAwait(true));

            Status = "E-way bill cancelled at the portal.";
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
    private async Task ExtendAsync(CancellationToken cancellationToken)
    {
        if (await RequireBillAsync(cancellationToken).ConfigureAwait(true) is not { } bill)
        {
            return;
        }

        if (!int.TryParse(DistanceKilometres, CultureInfo.CurrentCulture, out var remaining)
            || remaining < 0)
        {
            Status = "Enter the remaining distance in kilometres.";
            return;
        }

        IsBusy = true;

        try
        {
            Apply(await _ewayBills
                .ExtendValidityAsync(bill.Id, remaining, ReasonCode.Trim(), NullIfEmpty(Remarks), cancellationToken)
                .ConfigureAwait(true));

            Status = $"Validity extended to {ValidUntil}.";
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
        VehicleNumber = string.Empty;
        TransporterGstin = string.Empty;
        TransporterName = string.Empty;
        TransportDocumentNumber = string.Empty;
        TransportDocumentDate = null;
        Status = "Cleared.";
    }

    private async Task<EwayBillSummary?> RequireBillAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice)
        {
            Status = "Pick a document first.";
            return null;
        }

        var bill = await _ewayBills.FindByDocumentAsync(invoice.DocumentId, cancellationToken)
            .ConfigureAwait(true);

        if (bill is null)
        {
            Status = "This document has no e-way bill yet.";
        }

        return bill;
    }

    private bool TryBuildPartB(out EwayBillPartB partB, out int distance, out string problem)
    {
        partB = new EwayBillPartB(TransportMode.Road, VehicleType.Regular);
        distance = 0;
        problem = string.Empty;

        if (!int.TryParse(DistanceKilometres, CultureInfo.CurrentCulture, out distance) || distance < 0)
        {
            problem = "Enter the approximate distance in kilometres.";
            return false;
        }

        var mode = Enum.Parse<TransportMode>(SelectedTransportMode);
        var vehicleType = Enum.Parse<VehicleType>(SelectedVehicleType);

        if (mode is TransportMode.Road && string.IsNullOrWhiteSpace(VehicleNumber))
        {
            problem = "Road transport needs a vehicle number.";
            return false;
        }

        if (mode is not TransportMode.Road
            && (string.IsNullOrWhiteSpace(TransportDocumentNumber) || TransportDocumentDate is null))
        {
            problem = "Rail, air and ship transport need a transport document number and date.";
            return false;
        }

        partB = new EwayBillPartB(
            mode,
            vehicleType,
            NullIfEmpty(VehicleNumber),
            NullIfEmpty(TransporterGstin),
            NullIfEmpty(TransporterName),
            NullIfEmpty(TransportDocumentNumber),
            TransportDocumentDate is { } date ? DateOnly.FromDateTime(date) : null);

        return true;
    }

    private async Task LoadDetailAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            Requirement = await _ewayBills.IsRequiredForDocumentAsync(documentId, cancellationToken)
                .ConfigureAwait(true)
                ? "Above the configured threshold — an e-way bill is required."
                : "At or below the configured threshold — no e-way bill required for this document alone.";

            if (await _ewayBills.FindByDocumentAsync(documentId, cancellationToken).ConfigureAwait(true)
                is { } bill)
            {
                Apply(bill);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
    }

    private void Apply(EwayBillSummary bill)
    {
        BillNumber = bill.EwayBillNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        BillStatus = bill.Status.ToString();
        ValidUntil = bill.ValidUntilUtc is { } until
            ? until.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture)
            : "—";
        DistanceKilometres = bill.DistanceKilometres.ToString(CultureInfo.CurrentCulture);
        SelectedVehicleType = bill.VehicleType.ToString();

        if (bill.TransportMode is { } mode)
        {
            SelectedTransportMode = mode.ToString();
        }

        VehicleNumber = bill.VehicleNumber ?? string.Empty;
        TransporterGstin = bill.TransporterGstin ?? string.Empty;
        TransporterName = bill.TransporterName ?? string.Empty;
        TransportDocumentNumber = bill.TransportDocumentNumber ?? string.Empty;
        TransportDocumentDate = bill.TransportDocumentDate is { } date
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;

        CanCancel = bill.Status is EwayBillStatus.Active
            && bill.GeneratedAtUtc is { } generated
            && DateTimeOffset.UtcNow <= generated + EwayBill.CancellationWindow;

        if (bill.LastErrorText is { Length: > 0 } error)
        {
            Status = error;
        }
    }

    /// <summary>
    /// Shows the validity the distance slabs imply, before the portal is asked. Useful precisely
    /// because over-dimensional cargo earns a tenth of the validity per kilometre, which surprises
    /// people.
    /// </summary>
    private void RecomputeExpectedValidity()
    {
        if (!int.TryParse(DistanceKilometres, CultureInfo.CurrentCulture, out var distance)
            || distance < 0
            || !Enum.TryParse<VehicleType>(SelectedVehicleType, out var vehicleType))
        {
            ExpectedValidity = string.Empty;
            return;
        }

        var days = EwayBillValidity.DaysFor(distance, vehicleType);

        ExpectedValidity = $"{days} day(s) expected for {distance} km ({vehicleType}).";
    }

    private void ClearDetail()
    {
        BillNumber = string.Empty;
        BillStatus = string.Empty;
        ValidUntil = string.Empty;
        Requirement = string.Empty;
        CanCancel = false;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One movable document and its e-way bill state.</summary>
public sealed record EwayBillInvoiceRow(
    Guid DocumentId,
    string Number,
    DateOnly DocumentDate,
    string PartyName,
    decimal TotalNetValue,
    string EwayBillState);
