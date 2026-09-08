using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Computes invoice GST from data — HSN/SAC assignment on the item, the rate schedule effective on
/// the document date, and place-of-supply resolution — and stamps the result onto the aggregate
/// (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Nothing about tax is hardcoded: rates come from <see cref="GstRateSchedule"/> rows, codes from
/// <see cref="HsnSac"/>. An item with no code or no covering schedule computes at zero with a
/// warning rather than an error — the owner may raise non-GST invoices while unconfigured
/// (spec §4a). The round-off difference is computed per invoice to GST convention and routed to the
/// ledger named in <see cref="GstSettings"/> ("Gst.RoundOffLedger"), never dropped.
/// </remarks>
public sealed class GstComputationService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IGstComputationService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<InvoiceTaxComputationResult> ComputeAndApplyAsync(
        TradeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var companyId = document.CompanyId == Guid.Empty
            ? await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false)
            : document.CompanyId;

        var company = await RequireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var settings = (await _unitOfWork.Repository<GstSettings>()
                .ListAsync(new SettingsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

        var roundOffLedgerName = settings?.RoundOffLedgerName ?? GstSettings.DefaultRoundOffLedgerName;

        var placeOfSupply = await ResolvePlaceOfSupplyAsync(
                companyId, document, company.StateCode, cancellationToken)
            .ConfigureAwait(false);

        var isIntraState = string.Equals(
            placeOfSupply,
            company.StateCode,
            StringComparison.Ordinal);

        var itemsById = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id);

        var schedulesByCode = (await _unitOfWork.Repository<GstRateSchedule>()
                .ListAsync(new SchedulesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(schedule => schedule.HsnSacId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var warnings = new List<string>();
        var breakdowns = new List<GstLineTaxBreakdown>(document.Lines.Count);

        var itemIdsWithCode = itemsById.Values
            .Where(item => item.HsnSacId.HasValue)
            .Select(item => item.HsnSacId!.Value)
            .Distinct()
            .ToList();

        var codeDescriptions = itemIdsWithCode.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _unitOfWork.Repository<HsnSac>()
                    .ListAsync(new CodesByIds(itemIdsWithCode), cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(code => code.Id, code => code.Code);

        long totalTaxable = 0L;
        long totalIgst = 0L;
        long totalCgst = 0L;
        long totalSgst = 0L;
        long totalCess = 0L;

        foreach (var line in document.Lines.OrderBy(line => line.LineNumber))
        {
            if (!itemsById.TryGetValue(line.ItemId, out var item))
            {
                throw new InvalidOperationException(
                    $"Item '{line.ItemId}' on this invoice does not exist in this company's books.");
            }

            long taxable = MoneyScale.ToMinor(MoneyScale.Round(line.NetValue));
            long ratePercentMinor = 0L;
            long igst = 0L;
            long cgst = 0L;
            long sgst = 0L;
            long cess = 0L;

            if (item.HsnSacId.HasValue)
            {
                var schedule = schedulesByCode.GetValueOrDefault(item.HsnSacId.Value, [])
                    .SingleOrDefault(candidate => candidate.Covers(document.DocumentDate));

                if (schedule is null)
                {
                    warnings.Add(
                        $"No GST rate schedule covers '{item.Name}' (HSN/SAC effective "
                        + $"{document.DocumentDate:yyyy-MM-dd}); its lines compute at zero tax.");
                }
                else
                {
                    ratePercentMinor = schedule.RatePercentMinor;
                    var totalTax = GstTaxMath.TaxMinor(taxable, schedule.RatePercentMinor);
                    cess = GstTaxMath.TaxMinor(taxable, schedule.CompensationCessPercentMinor);

                    if (isIntraState)
                    {
                        (cgst, sgst) = GstTaxMath.SplitHalf(totalTax);
                    }
                    else
                    {
                        igst = totalTax;
                    }
                }
            }
            else
            {
                warnings.Add(
                    $"'{item.Name}' carries no HSN/SAC code; its lines compute at zero tax. Assign " +
                    "the code on the item master before filing returns that expect it.");
            }

            // Reverse charge: nothing is billed to the party; the self-assessed figures still land
            // on the snapshot so GSTR-3B reports them, while the posting layer books them as
            // self-assessed output with matching input credit rather than party billing.
            if (document.IsReverseCharge)
            {
                igst = 0L;
                cgst = 0L;
                sgst = 0L;
                cess = 0L;
            }

            line.AttachTaxSnapshot(taxable, ratePercentMinor, igst, cgst, sgst, cess);

            totalTaxable += taxable;
            totalIgst += igst;
            totalCgst += cgst;
            totalSgst += sgst;
            totalCess += cess;

            breakdowns.Add(new GstLineTaxBreakdown(
                line.Id,
                line.ItemId,
                item.HsnSacId,
                item.HsnSacId.HasValue ? codeDescriptions.GetValueOrDefault(item.HsnSacId.Value) : null,
                taxable,
                ratePercentMinor,
                igst,
                cgst,
                sgst,
                cess));
        }

        document.AttachGstSnapshot(placeOfSupply, document.IsReverseCharge);

        var invoiceValue = totalTaxable + totalIgst + totalCgst + totalSgst + totalCess;
        var roundOff = GstTaxMath.RupeeRoundOffMinor(invoiceValue);

        return new InvoiceTaxComputationResult(
            placeOfSupply,
            document.IsReverseCharge,
            isIntraState,
            breakdowns,
            totalTaxable,
            totalIgst,
            totalCgst,
            totalSgst,
            totalCess,
            invoiceValue,
            invoiceValue + roundOff,
            roundOff,
            roundOffLedgerName,
            warnings);
    }

    /// <summary>
    /// Place of supply drives the CGST/SGST vs IGST split, not the billing address alone (§2):
    /// the document's own state wins, then the customer master's, then the leading digits of a
    /// keyed party GSTIN (the purchase side has no supplier master), then the company's own state.
    /// </summary>
    private async Task<string> ResolvePlaceOfSupplyAsync(
        Guid companyId,
        TradeDocument document,
        string companyStateCode,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(document.PlaceOfSupplyStateCode))
        {
            return document.PlaceOfSupplyStateCode.Trim();
        }

        var customer = (await _unitOfWork.Repository<Customer>()
                .ListAsync(new CustomerByLedger(companyId, document.PartyLedgerId), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

        if (!string.IsNullOrWhiteSpace(customer?.PlaceOfSupplyStateCode))
        {
            return customer.PlaceOfSupplyStateCode!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(document.PartyGstin) && document.PartyGstin!.Length >= 2)
        {
            return document.PartyGstin[..2];
        }

        return companyStateCode;
    }

    private async Task<Company> RequireCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        return company ?? throw new InvalidOperationException(
            $"Company '{companyId}' does not exist, so there is no registration to compute GST against.");
    }

    private sealed class SettingsOfCompany : Specification<GstSettings>
    {
        public SettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
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

    private sealed class SchedulesOfCompany : Specification<GstRateSchedule>
    {
        public SchedulesOfCompany(Guid companyId)
        {
            Where(schedule => schedule.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class CustomerByLedger : Specification<Customer>
    {
        public CustomerByLedger(Guid companyId, Guid ledgerId)
        {
            Where(customer => customer.CompanyId == companyId && customer.LedgerId == ledgerId);
            ApplyNoTracking();
        }
    }

    private sealed class CodesByIds : Specification<HsnSac>
    {
        public CodesByIds(IReadOnlyList<Guid> ids)
        {
            Where(code => ids.Contains(code.Id));
            ApplyNoTracking();
        }
    }
}
