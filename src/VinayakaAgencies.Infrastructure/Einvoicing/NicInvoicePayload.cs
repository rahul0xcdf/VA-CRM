using System.Globalization;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// Maps an <see cref="EinvoiceRequest"/> onto NIC's invoice JSON (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the portal's field names exist, and the one place minor units become
/// decimals — converted once, here, at the boundary, exactly as ADR-0003 requires. Nothing upstream
/// of this type knows that <c>AssAmt</c> is what the portal calls a taxable value.
/// </para>
/// <para>
/// The supply type is derived rather than asked for: a buyer with a GSTIN is B2B, and NIC's
/// unregistered-party placeholder means B2C. Since §4a puts exports, SEZ and e-commerce out of scope,
/// those are the only two values that can arise, and inventing a setting for it would invite someone
/// to set it wrongly.
/// </para>
/// <para>
/// <b>Unverified against a live portal</b> while <c>open-questions.md</c> #10 is open. The field names
/// follow NIC's published schema; checking them against the validator is what C6.1 is for.
/// </para>
/// </remarks>
internal sealed record NicInvoicePayload
{
    /// <summary>The e-invoice schema version this payload is shaped for.</summary>
    public const string SchemaVersion = "1.1";

    public string Version { get; init; } = SchemaVersion;

    public required NicTransactionDetails TranDtls { get; init; }

    public required NicDocumentDetails DocDtls { get; init; }

    public required NicPartyDetails SellerDtls { get; init; }

    public required NicPartyDetails BuyerDtls { get; init; }

    public required IReadOnlyList<NicInvoiceItem> ItemList { get; init; }

    public required NicValueDetails ValDtls { get; init; }

    public static NicInvoicePayload From(EinvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isB2B = !string.Equals(
            request.Buyer.Gstin, SignedQrPayload.UnregisteredParty, StringComparison.OrdinalIgnoreCase);

        return new NicInvoicePayload
        {
            TranDtls = new NicTransactionDetails
            {
                SupTyp = isB2B ? "B2B" : "B2C",
                RegRev = request.IsReverseCharge ? "Y" : "N",
            },
            DocDtls = new NicDocumentDetails
            {
                Typ = EinvoiceDocumentTypeCodes.Code(request.DocumentType),
                No = request.DocumentNumber,
                Dt = FormatDate(request.DocumentDate),
            },
            SellerDtls = NicPartyDetails.From(request.Seller),
            BuyerDtls = NicPartyDetails.From(request.Buyer) with
            {
                Pos = request.PlaceOfSupplyStateCode,
            },
            ItemList = [.. request.Lines.Select((line, index) => NicInvoiceItem.From(line, index + 1))],
            ValDtls = NicValueDetails.From(request),
        };
    }

    /// <summary>The portal wants <c>dd/MM/yyyy</c>, invariantly.</summary>
    internal static string FormatDate(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Minor units to the decimal the portal reads. The single conversion point (ADR-0003).</summary>
    internal static decimal Amount(long minor) => MoneyScale.FromMinor(minor);
}

/// <summary>NIC's <c>TranDtls</c> block.</summary>
internal sealed record NicTransactionDetails
{
    /// <summary>Always GST — §4a rules out every other tax scheme.</summary>
    public string TaxSch { get; init; } = "GST";

    public required string SupTyp { get; init; }

    /// <summary>Whether tax is payable under reverse charge.</summary>
    public required string RegRev { get; init; }
}

/// <summary>NIC's <c>DocDtls</c> block.</summary>
internal sealed record NicDocumentDetails
{
    public required string Typ { get; init; }

    public required string No { get; init; }

    public required string Dt { get; init; }
}

/// <summary>NIC's seller and buyer blocks, which share a shape.</summary>
internal sealed record NicPartyDetails
{
    public required string Gstin { get; init; }

    public required string LglNm { get; init; }

    public string? Addr1 { get; init; }

    public string? Loc { get; init; }

    public int? Pin { get; init; }

    public string? Stcd { get; init; }

    /// <summary>Place of supply — set on the buyer block only.</summary>
    public string? Pos { get; init; }

    public static NicPartyDetails From(EinvoicePartyDetails party)
    {
        ArgumentNullException.ThrowIfNull(party);

        return new NicPartyDetails
        {
            Gstin = party.Gstin,
            LglNm = party.LegalName,
            Addr1 = party.Address,
            Loc = party.City,
            Pin = party.Pincode,
            Stcd = party.StateCode,
        };
    }
}

/// <summary>One entry of NIC's <c>ItemList</c>.</summary>
internal sealed record NicInvoiceItem
{
    public required string SlNo { get; init; }

    /// <summary>
    /// Goods or service. Derived from the code length: SAC codes are six digits, HSN codes are four,
    /// six or eight, and NIC's own validator treats a six-digit code as either. Rather than guess, the
    /// item master's code is taken at face value and this is reported as goods unless the code is a
    /// SAC-shaped one beginning with 99, which is the service chapter.
    /// </summary>
    public required string IsServc { get; init; }

    public required string HsnCd { get; init; }

    public required decimal Qty { get; init; }

    public required string Unit { get; init; }

    public required decimal UnitPrice { get; init; }

    /// <summary>Gross amount before discount.</summary>
    public required decimal TotAmt { get; init; }

    /// <summary>Assessable (taxable) value.</summary>
    public required decimal AssAmt { get; init; }

    public required decimal GstRt { get; init; }

    public required decimal IgstAmt { get; init; }

    public required decimal CgstAmt { get; init; }

    public required decimal SgstAmt { get; init; }

    public required decimal CesAmt { get; init; }

    public required decimal TotItemVal { get; init; }

    public static NicInvoiceItem From(EinvoiceLine line, int serialNumber)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new NicInvoiceItem
        {
            SlNo = serialNumber.ToString(CultureInfo.InvariantCulture),
            IsServc = line.HsnCode.StartsWith("99", StringComparison.Ordinal) ? "Y" : "N",
            HsnCd = line.HsnCode,
            Qty = NicInvoicePayload.Amount(line.QuantityMinor),
            Unit = line.UnitCode,
            UnitPrice = NicInvoicePayload.Amount(line.UnitPriceMinor),
            TotAmt = NicInvoicePayload.Amount(line.TaxableValueMinor),
            AssAmt = NicInvoicePayload.Amount(line.TaxableValueMinor),
            GstRt = NicInvoicePayload.Amount(line.GstRatePercentMinor),
            IgstAmt = NicInvoicePayload.Amount(line.IgstMinor),
            CgstAmt = NicInvoicePayload.Amount(line.CgstMinor),
            SgstAmt = NicInvoicePayload.Amount(line.SgstMinor),
            CesAmt = NicInvoicePayload.Amount(line.CessMinor),
            TotItemVal = NicInvoicePayload.Amount(line.TotalValueMinor),
        };
    }
}

/// <summary>NIC's <c>ValDtls</c> block — the invoice totals.</summary>
/// <remarks>
/// Every figure here is a sum of the posted line snapshots, never a recomputation from rates. That is
/// what keeps the portal's copy of the invoice and the trial balance in agreement: they are summing
/// the same stored numbers.
/// </remarks>
internal sealed record NicValueDetails
{
    public required decimal AssVal { get; init; }

    public required decimal CgstVal { get; init; }

    public required decimal SgstVal { get; init; }

    public required decimal IgstVal { get; init; }

    public required decimal CesVal { get; init; }

    public required decimal RndOffAmt { get; init; }

    public required decimal TotInvVal { get; init; }

    public static NicValueDetails From(EinvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new NicValueDetails
        {
            AssVal = NicInvoicePayload.Amount(request.Lines.Sum(line => line.TaxableValueMinor)),
            CgstVal = NicInvoicePayload.Amount(request.Lines.Sum(line => line.CgstMinor)),
            SgstVal = NicInvoicePayload.Amount(request.Lines.Sum(line => line.SgstMinor)),
            IgstVal = NicInvoicePayload.Amount(request.Lines.Sum(line => line.IgstMinor)),
            CesVal = NicInvoicePayload.Amount(request.Lines.Sum(line => line.CessMinor)),
            RndOffAmt = NicInvoicePayload.Amount(request.RoundOffMinor),
            TotInvVal = NicInvoicePayload.Amount(EinvoiceQrBuilder.TotalInvoiceValueMinor(request)),
        };
    }
}
