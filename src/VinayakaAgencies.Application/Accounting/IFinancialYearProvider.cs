using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Resolves the financial year a business date falls in, creating it on first use.
/// </summary>
/// <remarks>
/// Financial years are created lazily on the first post into a year rather than provisioned ahead
/// (spec §12): the range is derivable from <c>Company.FinancialYearStartMonth</c> at any time, so
/// pre-creating rows would only invent a list of empty years somebody has to maintain. Creation is
/// an ordinary master change and is edit-logged like any other.
/// <para>
/// The caller owns the transaction. This provider adds the row to the unit of work and saves it
/// there; it never opens or commits a transaction of its own, because on a first post into a new
/// year the year and the voucher must land or fail together.
/// </para>
/// </remarks>
public interface IFinancialYearProvider
{
    /// <summary>
    /// The financial year containing <paramref name="businessDate"/> for this company, created if absent.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such company exists.</exception>
    Task<FinancialYear> GetOrCreateAsync(
        Guid companyId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
