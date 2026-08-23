namespace VinayakaAgencies.App;

/// <summary>One Indian state / union territory with its GST state code.</summary>
/// <remarks>
/// Static reference data by nature — like a calendar, these codes are statutory identifiers that do
/// not change per install, unlike rates and thresholds which stay config rows (CLAUDE.md
/// non-negotiable #6). The split matters: nothing here decides tax behaviour, it only labels where
/// the business sits; CGST/SGST versus IGST logic arrives with Phase 5's data-driven config.
/// </remarks>
public sealed record StateCode(string Code, string Name);

/// <summary>The GST state codes, in alphabetical order.</summary>
internal static class IndianStateCodes
{
    internal static IReadOnlyList<StateCode> All { get; } =
    [
        new("35", "Andaman & Nicobar Islands"),
        new("37", "Andhra Pradesh"),
        new("12", "Arunachal Pradesh"),
        new("18", "Assam"),
        new("10", "Bihar"),
        new("04", "Chandigarh"),
        new("22", "Chhattisgarh"),
        new("26", "Dadra & Nagar Haveli and Daman & Diu"),
        new("07", "Delhi"),
        new("30", "Goa"),
        new("24", "Gujarat"),
        new("06", "Haryana"),
        new("02", "Himachal Pradesh"),
        new("01", "Jammu & Kashmir"),
        new("20", "Jharkhand"),
        new("29", "Karnataka"),
        new("32", "Kerala"),
        new("38", "Ladakh"),
        new("31", "Lakshadweep"),
        new("23", "Madhya Pradesh"),
        new("27", "Maharashtra"),
        new("14", "Manipur"),
        new("17", "Meghalaya"),
        new("15", "Mizoram"),
        new("13", "Nagaland"),
        new("21", "Odisha"),
        new("34", "Puducherry"),
        new("03", "Punjab"),
        new("08", "Rajasthan"),
        new("11", "Sikkim"),
        new("33", "Tamil Nadu"),
        new("36", "Telangana"),
        new("16", "Tripura"),
        new("09", "Uttar Pradesh"),
        new("05", "Uttarakhand"),
        new("19", "West Bengal"),
    ];
}
