using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Resolves the financial year a business date falls in, creating it on first use (spec §12).
/// </summary>
/// <remarks>
/// Years are derived from <see cref="Company.FinancialYearStartMonth"/> (4 = April, decision D1)
/// rather than provisioned ahead: the range is computable at any time, so a pre-built list would
/// only be a second place to be wrong. Creation is an ordinary master change through the unit of
/// work, so it is edit-logged like every other insert.
/// <para>
/// This class never opens a transaction. It participates in the caller's, because a first post
/// into a new year must write the year and the voucher together or not at all.
/// </para>
/// </remarks>
public sealed class FinancialYearProvider(IUnitOfWork unitOfWork) : IFinancialYearProvider
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    /// <inheritdoc />
    public async Task<FinancialYear> GetOrCreateAsync(
        Guid companyId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        if (company is null)
        {
            throw new InvalidOperationException(
                $"Company '{companyId}' does not exist, so no financial year can be resolved.");
        }

        var (start, end) = FinancialYear.RangeFor(businessDate, company.FinancialYearStartMonth);

        var repository = _unitOfWork.Repository<FinancialYear>();

        var existing = await repository
            .SingleOrDefaultAsync(new FinancialYearOfCompanyStartingAt(companyId, start), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var financialYear = new FinancialYear(companyId, start, end);
        await repository.AddAsync(financialYear, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return financialYear;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinancialYear>> ListAsync(CancellationToken cancellationToken = default)
    {
        var companies = await _unitOfWork.Repository<Company>()
            .ListAsync(new ActiveCompanies(), cancellationToken)
            .ConfigureAwait(false);

        if (companies.Count == 0)
        {
            throw new InvalidOperationException(
                "This install has no active company, so there are no financial years to list.");
        }

        // The same oldest-active-company rule SingleCompanyContext applies, so both services always
        // mean the same books even after a second company appears.
        var companyId = companies.OrderBy(company => company.CreatedAtUtc).First().Id;

        var years = await _unitOfWork.Repository<FinancialYear>()
            .ListAsync(new YearsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        return years
            .OrderByDescending(year => year.StartDate)
            .ToList();
    }

    private sealed class ActiveCompanies : Specification<Company>
    {
        internal ActiveCompanies()
        {
            Where(company => company.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class YearsOfCompany : Specification<FinancialYear>
    {
        public YearsOfCompany(Guid companyId)
        {
            Where(year => year.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class FinancialYearOfCompanyStartingAt : Specification<FinancialYear>
    {
        public FinancialYearOfCompanyStartingAt(Guid companyId, DateOnly startDate)
        {
            Where(year => year.CompanyId == companyId && year.StartDate == startDate);
            ApplyNoTracking();
        }
    }
}
