using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// Maintains the customer categories through the unit of work
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Deletion follows the inventory-masters rule: available only while nothing in the books
/// references the row, because a category's customers and price lines must never be orphaned —
/// the foreign keys backstop what the checks here express.
/// </remarks>
public sealed class CustomerCategoryService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : ICustomerCategoryService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerCategorySummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var categories = await _unitOfWork.Repository<CustomerCategory>()
            .ListAsync(new CategoriesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var counts = await CountCustomersByCategoryAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        return categories
            .Where(category => includeInactive || category.IsActive)
            .OrderBy(category => category.NameNormalised, StringComparer.Ordinal)
            .Select(category => ToSummary(category, counts.GetValueOrDefault(category.Id)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CustomerCategorySummary?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var category = await RequireCategoryAsync(companyId, id, cancellationToken)
            .ConfigureAwait(false);

        var counts = await CountCustomersByCategoryAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        return ToSummary(category, counts.GetValueOrDefault(category.Id));
    }

    /// <inheritdoc />
    public async Task<CustomerCategorySummary> SaveAsync(
        SaveCustomerCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        CustomerCategory category;

        if (request.Id.HasValue)
        {
            category = await RequireCategoryAsync(companyId, request.Id.Value, cancellationToken)
                .ConfigureAwait(false);

            await RequireUniqueNameAsync(companyId, request.Name, category.Id, cancellationToken)
                .ConfigureAwait(false);

            category.Rename(request.Name);
            category.SetDescription(request.Description);
        }
        else
        {
            await RequireUniqueNameAsync(companyId, request.Name, null, cancellationToken)
                .ConfigureAwait(false);

            category = new CustomerCategory(companyId, request.Name, request.Description);
            await _unitOfWork.Repository<CustomerCategory>()
                .AddAsync(category, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var counts = await CountCustomersByCategoryAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        return ToSummary(category, counts.GetValueOrDefault(category.Id));
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var category = await RequireCategoryAsync(companyId, id, cancellationToken)
            .ConfigureAwait(false);

        if (await _unitOfWork.Repository<Customer>()
                .AnyAsync(new CustomersInCategory(id), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{category.Name}' still has customers; move them to another category before "
                + "deactivating it.");
        }

        category.Deactivate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireCategoryAsync(companyId, id, cancellationToken).ConfigureAwait(false))
            .Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var category = await RequireCategoryAsync(companyId, id, cancellationToken)
            .ConfigureAwait(false);

        if (await _unitOfWork.Repository<Customer>()
                .AnyAsync(new CustomersInCategory(id), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{category.Name}' still has customers; move or deactivate them before "
                + "deleting the category.");
        }

        if (await _unitOfWork.Repository<CategoryPriceListLine>()
                .AnyAsync(new PriceLinesOfCategory(id), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{category.Name}' still has price-list lines; remove them before deleting "
                + "the category.");
        }

        _unitOfWork.Repository<CustomerCategory>().Remove(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CustomerCategorySummary ToSummary(CustomerCategory category, int customerCount) =>
        new(category.Id, category.Name, category.Description, category.IsActive, customerCount);

    private async Task<CustomerCategory> RequireCategoryAsync(
        Guid companyId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var category = await _unitOfWork.Repository<CustomerCategory>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (category is null || category.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                "This customer category does not exist in this company's books.");
        }

        return category;
    }

    private async Task RequireUniqueNameAsync(
        Guid companyId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(name);

        // Judged on the normalised form exactly as the unique index will judge it, so "Dealer"
        // and "dealer" collide here before they ever reach the database.
        var clash = (await _unitOfWork.Repository<CustomerCategory>()
                .ListAsync(new CategoriesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Any(category => category.Id != exceptId && category.NameNormalised == normalised);

        if (clash)
        {
            throw new InvalidOperationException($"A category named '{name}' already exists.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid, int>> CountCustomersByCategoryAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<Customer>()
                .ListAsync(new CustomersOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(customer => customer.CustomerCategoryId)
            .ToDictionary(group => group.Key, group => group.Count());

    private sealed class CategoriesOfCompany : Specification<CustomerCategory>
    {
        public CategoriesOfCompany(Guid companyId)
        {
            Where(category => category.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class CustomersOfCompany : Specification<Customer>
    {
        public CustomersOfCompany(Guid companyId)
        {
            Where(customer => customer.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class CustomersInCategory : Specification<Customer>
    {
        public CustomersInCategory(Guid categoryId)
        {
            Where(customer => customer.CustomerCategoryId == categoryId);
            ApplyNoTracking();
        }
    }

    private sealed class PriceLinesOfCategory : Specification<CategoryPriceListLine>
    {
        public PriceLinesOfCategory(Guid categoryId)
        {
            Where(line => line.CustomerCategoryId == categoryId);
            ApplyNoTracking();
        }
    }
}
