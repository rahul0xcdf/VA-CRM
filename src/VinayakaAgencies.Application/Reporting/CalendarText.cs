using System.Globalization;

namespace VinayakaAgencies.Application.Reporting;

/// <summary>
/// Renders business dates as the canonical calendar text the database stores (<c>yyyy-MM-dd</c>).
/// </summary>
/// <remarks>
/// Dapper cannot bind <see cref="DateOnly"/> directly, and the reporting statements compare dates
/// as text anyway — EF Core writes every <see cref="DateOnly"/> column in exactly this form, so a
/// parameter in this form compares correctly against them on any provider. Business dates are local
/// calendar facts; the invariant culture keeps 1 April in April regardless of host settings.
/// </remarks>
internal static class CalendarText
{
    public static string For(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
