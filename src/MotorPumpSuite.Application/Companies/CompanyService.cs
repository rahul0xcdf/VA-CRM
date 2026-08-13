using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Entities;

namespace MotorPumpSuite.Application.Companies;

/// <summary>
/// Company master operations, executed through the unit of work.
/// </summary>
/// <remarks>
/// Deliberately thin. Phase 0 exists to prove the seam — Application → <see cref="IUnitOfWork"/> →
/// <see cref="IRepository{TEntity}"/> → database — with no business rules attached. The real masters
/// and the rules that govern them arrive in Phase 1.
/// </remarks>
public sealed class CompanyService(IUnitOfWork unitOfWork) : ICompanyService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    public async Task<IReadOnlyList<CompanySummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var companies = await _unitOfWork.Repository<Company>()
            .ListAsync(new CompaniesByNameSpecification(), cancellationToken)
            .ConfigureAwait(false);

        return companies.Select(ToSummary).ToList();
    }

    public async Task<CompanySummary?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return company is null ? null : ToSummary(company);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.Repository<Company>().CountAsync(cancellationToken: cancellationToken);

    public async Task<CompanySummary> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var company = new Company(
            request.Name,
            request.LegalName,
            request.StateCode,
            request.Gstin,
            request.FinancialYearStartMonth,
            request.BaseCurrencyCode);

        await _unitOfWork.Repository<Company>()
            .AddAsync(company, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(company);
    }

    private static CompanySummary ToSummary(Company company) => new(
        company.Id,
        company.Name,
        company.LegalName,
        company.Gstin,
        company.StateCode,
        company.FinancialYearStartMonth,
        company.IsActive);

    private sealed class CompaniesByNameSpecification : Specification<Company>
    {
        public CompaniesByNameSpecification()
        {
            ApplyOrderBy(c => c.Name);
            ApplyNoTracking();
        }
    }
}
