using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// Maps Part-B onto NIC's e-way bill JSON (phase-6-einvoice-eway.md §2, §4a).
/// </summary>
/// <remarks>
/// <para>
/// Generating by IRN is why this payload is so small: the portal already holds the invoice, so only
/// the transport details and the distance travel. That is the route the spec asks for, and it avoids
/// a second chance for the invoice detail we send to diverge from the invoice we registered.
/// </para>
/// <para>
/// <b>Unverified against a live portal</b> while <c>open-questions.md</c> #10 is open — confirming
/// these field names is part of C6.2.
/// </para>
/// </remarks>
internal static class NicEwayBillPayload
{
    /// <summary>Generate-by-IRN body.</summary>
    public static object From(string irn, EwayBillPartB partB, int distanceKilometres)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(irn);
        ArgumentNullException.ThrowIfNull(partB);

        return new
        {
            Irn = irn,
            Distance = distanceKilometres,
            TransMode = ModeCode(partB.TransportMode),
            VehNo = partB.VehicleNumber,
            VehType = VehicleTypeCode(partB.VehicleType),
            TransId = partB.TransporterGstin,
            TransName = partB.TransporterName,
            TransDocNo = partB.TransportDocumentNumber,
            TransDocDt = partB.TransportDocumentDate is { } date
                ? NicInvoicePayload.FormatDate(date)
                : null,
        };
    }

    /// <summary>Part-B update body, addressed by e-way bill number rather than by IRN.</summary>
    public static object PartBUpdate(long ewayBillNumber, EwayBillPartB partB)
    {
        ArgumentNullException.ThrowIfNull(partB);

        return new
        {
            ewbNo = ewayBillNumber,
            vehicleNo = partB.VehicleNumber,
            fromPlace = (string?)null,
            fromState = (string?)null,
            reasonCode = "2",
            reasonRem = "Vehicle changed",
            transDocNo = partB.TransportDocumentNumber,
            transDocDate = partB.TransportDocumentDate is { } date
                ? NicInvoicePayload.FormatDate(date)
                : null,
            transMode = ModeCode(partB.TransportMode),
            vehicleType = VehicleTypeCode(partB.VehicleType),
        };
    }

    /// <summary>
    /// NIC's numeric transport-mode codes. Mapped here rather than stored as numbers so the domain
    /// keeps a readable enum and the wire format stays the portal's business.
    /// </summary>
    private static string ModeCode(TransportMode mode) => mode switch
    {
        TransportMode.Road => "1",
        TransportMode.Rail => "2",
        TransportMode.Air => "3",
        TransportMode.Ship => "4",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode."),
    };

    /// <summary>NIC's vehicle-type codes: regular, or over-dimensional cargo.</summary>
    private static string VehicleTypeCode(VehicleType vehicleType) => vehicleType switch
    {
        VehicleType.Regular => "R",
        VehicleType.OverDimensionalCargo => "O",
        _ => throw new ArgumentOutOfRangeException(
            nameof(vehicleType), vehicleType, "Unknown vehicle type."),
    };
}
