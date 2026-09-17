namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// How long an e-way bill is valid for a given distance and vehicle type
/// (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// One day per 200 km for regular carriage, and one day per 20 km for over-dimensional cargo. The
/// spec's scope note states only the 200 km figure, because that is the ordinary case; the ODC
/// figure is included because Part-B carries a vehicle type, and applying the regular rule to an ODC
/// consignment would overstate its validity by a factor of ten. Any part-day counts as a whole day,
/// and every bill gets at least one day — a 5 km local delivery is valid for a day, not for none.
/// </para>
/// <para>
/// These are the distance slabs of Rule 138, which is a rule rather than a rate: it does not change
/// by notification the way a GST rate does, so it is expressed here instead of as a config row.
/// The distance it is applied to, on the other hand, is per-consignment data on the bill.
/// </para>
/// </remarks>
public static class EwayBillValidity
{
    /// <summary>Kilometres per day of validity for regular carriage.</summary>
    public const int KilometresPerDayRegular = 200;

    /// <summary>Kilometres per day of validity for over-dimensional cargo.</summary>
    public const int KilometresPerDayOverDimensional = 20;

    /// <summary>Days of validity for <paramref name="distanceKilometres"/>, never fewer than one.</summary>
    public static int DaysFor(int distanceKilometres, VehicleType vehicleType)
    {
        if (distanceKilometres < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceKilometres),
                distanceKilometres,
                "A transport distance cannot be negative.");
        }

        var perDay = vehicleType switch
        {
            VehicleType.Regular => KilometresPerDayRegular,
            VehicleType.OverDimensionalCargo => KilometresPerDayOverDimensional,
            _ => throw new ArgumentOutOfRangeException(
                nameof(vehicleType), vehicleType, "Unknown vehicle type."),
        };

        // Integer ceiling: 201 km is two days, not one and a bit.
        var days = (distanceKilometres + perDay - 1) / perDay;

        return Math.Max(1, days);
    }

    /// <summary>
    /// The instant a bill generated at <paramref name="generatedAtUtc"/> expires.
    /// </summary>
    public static DateTimeOffset ExpiresAtUtc(
        DateTimeOffset generatedAtUtc,
        int distanceKilometres,
        VehicleType vehicleType) =>
        generatedAtUtc + TimeSpan.FromDays(DaysFor(distanceKilometres, vehicleType));
}
