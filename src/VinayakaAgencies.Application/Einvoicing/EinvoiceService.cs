using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Registers posted documents with the IRP, idempotently (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The ordering in <see cref="GenerateAsync"/> is the substance of this class, and it is deliberate.
/// The registration row — with its locally computed IRN — is committed <em>before</em> the portal is
/// called. That looks backwards until the failure case is considered: if the row were written after a
/// successful response, a call that timed out after the portal had registered the invoice would leave
/// us with no record of an IRN that now exists, and the obvious next move would be to submit again
/// and collect a duplicate rejection. Writing first means the IRN is always recoverable, and a retry
/// asks the portal about it rather than guessing (checkpoint C6.5).
/// </para>
/// <para>
/// Nothing here recomputes tax. The figures sent to the portal are the snapshot Phase 5 posted with
/// the invoice, so what the portal is told and what the trial balance says are the same numbers by
/// construction rather than by coincidence.
/// </para>
/// </remarks>
public sealed class EinvoiceService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    IEinvoiceSettingsService settingsService,
    IIrpClient irpClient,
    IClock clock) : IEinvoiceService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IEinvoiceSettingsService _settingsService = settingsService
        ?? throw new ArgumentNullException(nameof(settingsService));

    private readonly IIrpClient _irpClient = irpClient
        ?? throw new ArgumentNullException(nameof(irpClient));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<IReadOnlyList<EinvoiceRegistrationSummary>> ListAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<EinvoiceRegistration>()
                .ListAsync(new RegistrationsInRange(companyId, fromDate, toDate), cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(registration => registration.DocumentDate)
            .ThenByDescending(registration => registration.DocumentNumber)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<EinvoiceRegistrationSummary?> FindByDocumentAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var registration = await FindRegistrationAsync(companyId, tradeDocumentId, cancellationToken)
            .ConfigureAwait(false);

        return registration is null ? null : ToSummary(registration);
    }

    /// <inheritdoc />
    public async Task<EinvoiceRequest> BuildRequestAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return await AssembleAsync(companyId, tradeDocumentId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EinvoiceGenerationResult> GenerateAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var settings = await _settingsService.GetEntityAsync(cancellationToken).ConfigureAwait(false);

        if (!settings.IsEinvoicingApplicable)
        {
            throw new EinvoiceNotSubmittableException(
                "E-invoicing is switched off for this company. Turn it on in E-invoice settings if "
                + "the business is above the turnover threshold.");
        }

        var credentials = await _settingsService.ResolveCredentialsAsync(cancellationToken)
            .ConfigureAwait(false);

        var request = await AssembleAsync(companyId, tradeDocumentId, cancellationToken)
            .ConfigureAwait(false);

        var registration = await FindRegistrationAsync(companyId, tradeDocumentId, cancellationToken)
            .ConfigureAwait(false);

        // Already done: say so and call nothing. A portal round trip here would be a duplicate
        // submission dressed up as a refresh.
        if (registration is { Status: EinvoiceStatus.Generated })
        {
            return new EinvoiceGenerationResult(ToSummary(registration), true, true);
        }

        if (registration is { Status: EinvoiceStatus.Cancelled })
        {
            throw new EinvoiceNotSubmittableException(
                "This invoice's e-invoice was cancelled at the portal. The same IRN cannot be "
                + "re-registered; raise a fresh invoice.");
        }

        if (registration is null)
        {
            registration = new EinvoiceRegistration(
                companyId,
                tradeDocumentId,
                request.Seller.Gstin,
                request.DocumentType,
                request.DocumentNumber,
                request.DocumentDate);

            await _unitOfWork.Repository<EinvoiceRegistration>()
                .AddAsync(registration, cancellationToken)
                .ConfigureAwait(false);

            // Committed before the portal is called, on purpose — see the class remarks.
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (registration.AttemptCount > 0)
        {
            // An earlier attempt went unanswered. Ask the portal what it knows before submitting
            // anything: if it already holds this IRN, the previous call did land (checkpoint C6.5).
            var settled = await SettleUnresolvedAsync(
                    companyId, registration, credentials, cancellationToken)
                .ConfigureAwait(false);

            if (settled)
            {
                return new EinvoiceGenerationResult(ToSummary(registration), true, true);
            }
        }

        return await SubmitAsync(companyId, registration, request, credentials, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EinvoiceRegistrationSummary> CancelAsync(
        Guid registrationId,
        string reasonCode,
        string? remarks = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var registration = await _unitOfWork.Repository<EinvoiceRegistration>()
            .GetByIdAsync(registrationId, cancellationToken)
            .ConfigureAwait(false);

        if (registration is null || registration.CompanyId != companyId)
        {
            throw new InvalidOperationException("This e-invoice registration is not in this company's books.");
        }

        var now = _clock.UtcNow;

        // Checked before the call as well as inside MarkCancelled, so a closed window costs no round
        // trip and produces a message about credit notes rather than a portal error code.
        if (!registration.CanCancelAt(now))
        {
            throw new InvalidOperationException(
                "The 24-hour cancellation window has closed for this IRN. Issue a credit note instead.");
        }

        var credentials = await _settingsService.ResolveCredentialsAsync(cancellationToken)
            .ConfigureAwait(false);

        var call = new EinvoiceApiCall(
            companyId, registration.Id, "CancelIrn", DescribeEndpoint(credentials), string.Empty, now);

        var response = await _irpClient
            .CancelIrnAsync(credentials, registration.Irn, reasonCode, remarks, cancellationToken)
            .ConfigureAwait(false);

        await RecordCallAsync(call, response.Call, cancellationToken).ConfigureAwait(false);

        registration.RecordAttempt();

        if (response.Call.Succeeded)
        {
            registration.MarkCancelled(now, reasonCode, remarks);
        }
        else
        {
            registration.MarkUnresolved(
                response.Call.ErrorMessage ?? "The portal did not confirm the cancellation.");
        }

        _unitOfWork.Repository<EinvoiceRegistration>().Update(registration);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!response.Call.Succeeded)
        {
            throw new InvalidOperationException(
                $"The portal did not cancel this IRN: {response.Call.ErrorMessage}");
        }

        return ToSummary(registration);
    }

    /// <summary>
    /// Asks the portal about an IRN left unresolved by an earlier attempt. Returns true when the
    /// portal already holds it, in which case no submission happens at all.
    /// </summary>
    private async Task<bool> SettleUnresolvedAsync(
        Guid companyId,
        EinvoiceRegistration registration,
        IrpCredentials credentials,
        CancellationToken cancellationToken)
    {
        var call = new EinvoiceApiCall(
            companyId,
            registration.Id,
            "GetByIrn",
            DescribeEndpoint(credentials),
            string.Empty,
            _clock.UtcNow);

        var response = await _irpClient
            .GetByIrnAsync(credentials, registration.Irn, cancellationToken)
            .ConfigureAwait(false);

        await RecordCallAsync(call, response.Call, cancellationToken).ConfigureAwait(false);

        if (!response.Call.Succeeded || response.SignedQrCodeJws is null)
        {
            // Either the portal has never seen it, or it is still unreachable. Both mean "carry on
            // and submit"; the row stays retryable either way.
            return false;
        }

        ApplySuccess(registration, response);

        _unitOfWork.Repository<EinvoiceRegistration>().Update(registration);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private async Task<EinvoiceGenerationResult> SubmitAsync(
        Guid companyId,
        EinvoiceRegistration registration,
        EinvoiceRequest request,
        IrpCredentials credentials,
        CancellationToken cancellationToken)
    {
        var call = new EinvoiceApiCall(
            companyId,
            registration.Id,
            "GenerateIrn",
            DescribeEndpoint(credentials),
            string.Empty,
            _clock.UtcNow);

        registration.RecordAttempt();

        var response = await _irpClient
            .GenerateIrnAsync(credentials, request, cancellationToken)
            .ConfigureAwait(false);

        await RecordCallAsync(call, response.Call, cancellationToken).ConfigureAwait(false);

        bool? irnMatched = null;

        switch (response.Call.Outcome)
        {
            case IrpOutcome.Success:
                irnMatched = string.Equals(response.Irn, registration.Irn, StringComparison.OrdinalIgnoreCase);
                ApplySuccess(registration, response);
                break;

            case IrpOutcome.Rejected:
                registration.MarkRejected(
                    response.Call.ErrorMessage ?? "The portal rejected this document.");
                break;

            default:
                registration.MarkUnresolved(
                    response.Call.ErrorMessage ?? "No response from the portal.");
                break;
        }

        _unitOfWork.Repository<EinvoiceRegistration>().Update(registration);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new EinvoiceGenerationResult(ToSummary(registration), false, irnMatched);
    }

    /// <summary>
    /// Records what the portal returned. The portal's IRN is checked against ours and a mismatch is
    /// refused rather than stored: if the two disagree, one of the four hash inputs differs from what
    /// we believe it is, and printing the portal's IRN against our invoice would bury that.
    /// </summary>
    private static void ApplySuccess(EinvoiceRegistration registration, IrpIrnResponse response)
    {
        if (response.Irn is { Length: > 0 }
            && !string.Equals(response.Irn, registration.Irn, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The portal returned IRN '{response.Irn}' for a document we computed "
                + $"'{registration.Irn}' for. One of the four hash inputs disagrees; the invoice has "
                + "not been recorded as registered.");
        }

        registration.MarkGenerated(
            response.AcknowledgementNumber
                ?? throw new InvalidOperationException("The portal returned no acknowledgement number."),
            ParseAckDate(response.AcknowledgementDateText),
            response.AcknowledgementDateText!,
            response.SignedInvoiceJws
                ?? throw new InvalidOperationException("The portal returned no signed invoice."),
            response.SignedQrCodeJws
                ?? throw new InvalidOperationException("The portal returned no signed QR code."));
    }

    /// <summary>
    /// Parses the portal's <c>AckDt</c>. NIC sends it as Indian Standard Time without an offset, so
    /// the offset is supplied here rather than assumed to be the host's — a machine in another
    /// timezone would otherwise shift the 24-hour cancellation window.
    /// </summary>
    private static DateTimeOffset ParseAckDate(string? ackDateText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ackDateText);

        if (!DateTime.TryParse(
                ackDateText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidOperationException(
                $"The portal's acknowledgement date '{ackDateText}' is not a date we can read.");
        }

        return new DateTimeOffset(parsed, IndianStandardTimeOffset).ToUniversalTime();
    }

    /// <summary>IST, which is what NIC timestamps are in.</summary>
    private static readonly TimeSpan IndianStandardTimeOffset = new(5, 30, 0);

    private async Task RecordCallAsync(
        EinvoiceApiCall call,
        IrpCallRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Outcome is IrpOutcome.NoResponse)
        {
            call.MarkTimedOut(_clock.UtcNow);
        }
        else
        {
            call.Complete(
                record.HttpStatusCode ?? 0,
                record.RedactedResponseBody,
                _clock.UtcNow,
                record.ErrorCode);
        }

        await _unitOfWork.Repository<EinvoiceApiCall>().AddAsync(call, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string DescribeEndpoint(IrpCredentials credentials) =>
        credentials.Environment.ToString();

    private async Task<EinvoiceRegistration?> FindRegistrationAsync(
        Guid companyId,
        Guid tradeDocumentId,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<EinvoiceRegistration>()
            .SingleOrDefaultAsync(new RegistrationOfDocument(companyId, tradeDocumentId), cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Gathers everything the portal needs about a document from the books, refusing anything the
    /// portal would certainly reject before a call is made.
    /// </summary>
    private async Task<EinvoiceRequest> AssembleAsync(
        Guid companyId,
        Guid tradeDocumentId,
        CancellationToken cancellationToken)
    {
        var document = await _unitOfWork.Repository<TradeDocument>()
            .SingleOrDefaultAsync(new DocumentWithLines(companyId, tradeDocumentId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EinvoiceNotSubmittableException(
                "This document is not in this company's books.");

        if (document.Kind is not TradeDocumentKind.TaxInvoice)
        {
            throw new EinvoiceNotSubmittableException(
                "Only a tax invoice is registered with the IRP. Quotations, orders and challans are not.");
        }

        if (document.PostedVoucherId is null || string.IsNullOrWhiteSpace(document.Number))
        {
            throw new EinvoiceNotSubmittableException(
                "An invoice must be posted and numbered before it can be registered — the IRN is "
                + "computed from its number.");
        }

        if (document.Status is TradeDocumentStatus.Cancelled)
        {
            throw new EinvoiceNotSubmittableException("A cancelled invoice cannot be registered.");
        }

        if (document.Lines.Count is 0)
        {
            throw new EinvoiceNotSubmittableException("An e-invoice needs at least one line.");
        }

        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EinvoiceNotSubmittableException("This company no longer exists.");

        var seller = BuildSeller(company);
        var buyer = await BuildBuyerAsync(companyId, document, cancellationToken).ConfigureAwait(false);
        var lines = await BuildLinesAsync(companyId, document, cancellationToken).ConfigureAwait(false);

        return new EinvoiceRequest(
            EinvoiceDocumentType.TaxInvoice,
            document.Number!,
            document.DocumentDate,
            document.IsReverseCharge,
            seller,
            buyer,
            document.PlaceOfSupplyStateCode ?? company.StateCode,
            lines);
    }

    private static EinvoicePartyDetails BuildSeller(Company company)
    {
        if (string.IsNullOrWhiteSpace(company.Gstin))
        {
            throw new EinvoiceNotSubmittableException(
                "The company has no GSTIN. Set it in Company details before registering invoices.");
        }

        if (string.IsNullOrWhiteSpace(company.AddressLine1)
            || string.IsNullOrWhiteSpace(company.City)
            || company.Pincode is null)
        {
            throw new EinvoiceNotSubmittableException(
                "The company's address is incomplete. The IRP needs address line 1, city and "
                + "pincode — fill them in on Company details (phase-6 §4b).");
        }

        return new EinvoicePartyDetails(
            company.Gstin,
            company.LegalName,
            company.AddressLine1,
            company.City,
            company.StateCode,
            company.Pincode);
    }

    private async Task<EinvoicePartyDetails> BuildBuyerAsync(
        Guid companyId,
        TradeDocument document,
        CancellationToken cancellationToken)
    {
        var customer = await _unitOfWork.Repository<Customer>()
            .SingleOrDefaultAsync(new CustomerOfLedger(companyId, document.PartyLedgerId), cancellationToken)
            .ConfigureAwait(false);

        // An unregistered buyer is legitimate — NIC's own placeholder is "URP" — so a missing GSTIN
        // is not an error. A missing address still is, for anyone above the B2C value limit, and the
        // portal is the one that decides that, so incomplete addresses are refused here uniformly.
        var gstin = document.PartyGstin
            ?? customer?.Gstin
            ?? SignedQrPayload.UnregisteredParty;

        var legalName = customer?.Name
            ?? throw new EinvoiceNotSubmittableException(
                "This invoice's party is not a customer master row, so the IRP has no buyer name to "
                + "send. Link the party ledger to a customer first.");

        if (string.IsNullOrWhiteSpace(customer.City) || customer.Pincode is null)
        {
            throw new EinvoiceNotSubmittableException(
                $"Customer '{customer.Name}' has no city or pincode. The IRP needs both on the buyer "
                + "block — add them on the customer master (phase-6 §4b).");
        }

        return new EinvoicePartyDetails(
            gstin,
            legalName,
            FirstAddressLine(customer.BillingAddressLines),
            customer.City,
            customer.PlaceOfSupplyStateCode ?? document.PlaceOfSupplyStateCode,
            customer.Pincode);
    }

    private async Task<IReadOnlyList<EinvoiceLine>> BuildLinesAsync(
        Guid companyId,
        TradeDocument document,
        CancellationToken cancellationToken)
    {
        var items = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id);

        var codes = (await _unitOfWork.Repository<HsnSac>()
                .ListAsync(new CodesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(code => code.Id);

        var units = (await _unitOfWork.Repository<UnitOfMeasure>()
                .ListAsync(new UnitsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(unit => unit.Id);

        var lines = new List<EinvoiceLine>(document.Lines.Count);

        foreach (var line in document.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item))
            {
                throw new EinvoiceNotSubmittableException(
                    "A line on this invoice points at an item that no longer exists.");
            }

            if (item.HsnSacId is null || !codes.TryGetValue(item.HsnSacId.Value, out var code))
            {
                throw new EinvoiceNotSubmittableException(
                    $"Item '{item.Name}' has no HSN/SAC code. Every e-invoice line needs one — "
                    + "assign it on the item master.");
            }

            lines.Add(new EinvoiceLine(
                code.Code,
                item.Name,
                units.TryGetValue(item.BaseUnitId, out var unit) ? unit.Code : "OTH",
                line.QuantityMinor,
                line.AppliedRateMinor,
                line.TaxableValueMinor,
                line.GstRatePercentMinor,
                line.IgstMinor,
                line.CgstMinor,
                line.SgstMinor,
                line.CessMinor));
        }

        return lines;
    }

    private static string? FirstAddressLine(string? addressLines)
    {
        if (string.IsNullOrWhiteSpace(addressLines))
        {
            return null;
        }

        var lines = addressLines.Split(
            '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Length > 0 ? lines[0] : null;
    }

    private static EinvoiceRegistrationSummary ToSummary(EinvoiceRegistration registration) =>
        new(registration.Id,
            registration.TradeDocumentId,
            registration.DocumentNumber,
            registration.DocumentDate,
            EinvoiceDocumentTypeCodes.Code(registration.DocumentType),
            registration.Irn,
            registration.Status,
            registration.AcknowledgementNumber,
            registration.PortalAckDateText,
            registration.SignedQrCodeJws,
            registration.CancellableUntilUtc,
            registration.AttemptCount,
            registration.LastErrorText);

    private sealed class RegistrationsInRange : Specification<EinvoiceRegistration>
    {
        public RegistrationsInRange(Guid companyId, DateOnly? fromDate, DateOnly? toDate)
        {
            Where(registration => registration.CompanyId == companyId
                && (!fromDate.HasValue || registration.DocumentDate >= fromDate.Value)
                && (!toDate.HasValue || registration.DocumentDate <= toDate.Value));
            ApplyNoTracking();
        }
    }

    private sealed class RegistrationOfDocument : Specification<EinvoiceRegistration>
    {
        public RegistrationOfDocument(Guid companyId, Guid tradeDocumentId) =>
            Where(registration => registration.CompanyId == companyId
                && registration.TradeDocumentId == tradeDocumentId);
    }

    private sealed class DocumentWithLines : Specification<TradeDocument>
    {
        public DocumentWithLines(Guid companyId, Guid documentId)
        {
            Where(document => document.CompanyId == companyId && document.Id == documentId);
            AddInclude(document => document.Lines);
            ApplyNoTracking();
        }
    }

    private sealed class CustomerOfLedger : Specification<Customer>
    {
        public CustomerOfLedger(Guid companyId, Guid ledgerId)
        {
            Where(customer => customer.CompanyId == companyId && customer.LedgerId == ledgerId);
            ApplyNoTracking();
        }
    }

    private sealed class ItemsOfCompany : Specification<Item>
    {
        public ItemsOfCompany(Guid companyId)
        {
            Where(item => item.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class CodesOfCompany : Specification<HsnSac>
    {
        public CodesOfCompany(Guid companyId)
        {
            Where(code => code.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class UnitsOfCompany : Specification<UnitOfMeasure>
    {
        public UnitsOfCompany(Guid companyId)
        {
            Where(unit => unit.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
