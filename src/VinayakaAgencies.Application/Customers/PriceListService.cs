using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// Maintains the two price tables — the category price list and the per-customer special prices
/// (phase-4-customer-pricing.md §2, §4a) — through the unit of work.
/// </summary>
/// <remarks>
/// Both tables are keyed (category or customer) by item: at most one price line per pair, which
/// the unique indexes enforce and these services express as get-or-update. Every save validates
/// that the item exists and is active in this company's books; a dead item keeps its old lines for
/// history but takes no new ones.
/// </remarks>
public sealed class PriceListService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IPriceListService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceLineSummary>> ListCategoryPricesAsync(
        Guid customerCategoryId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await RequireCategoryAsync(companyId, customerCategoryId, cancellationToken)
            .ConfigureAwait(false);

        var lines = await _unitOfWork.Repository<CategoryPriceListLine>()
            .ListAsync(new CategoryLines(customerCategoryId), cancellationToken)
            .ConfigureAwait(false);

        return await ToSummariesAsync(
                companyId,
                lines.Select(line => (line.Id, line.CustomerCategoryId, line.ItemId, line.Basis,
                    line.FixedPrice, line.MarginPercent)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceLineSummary>> ListCustomerSpecialPricesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await RequireCustomerAsync(companyId, customerId, cancellationToken).ConfigureAwait(false);

        var lines = await _unitOfWork.Repository<CustomerSpecialPrice>()
            .ListAsync(new CustomerLines(customerId), cancellationToken)
            .ConfigureAwait(false);

        return await ToSummariesAsync(
                companyId,
                lines.Select(line => (line.Id, line.CustomerId, line.ItemId, line.Basis,
                    line.FixedPrice, line.MarginPercent)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PriceLineSummary> SetCategoryPriceAsync(
        SavePriceLineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await RequireCategoryAsync(companyId, request.OwnerId, cancellationToken).ConfigureAwait(false);
        var item = await RequireActiveItemAsync(companyId, request.ItemId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await FindCategoryLineAsync(request.OwnerId, request.ItemId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new CategoryPriceListLine(
                companyId,
                request.OwnerId,
                item.Id,
                request.Basis,
                request.FixedPrice,
                request.MarginPercent);

            await _unitOfWork.Repository<CategoryPriceListLine>()
                .AddAsync(existing, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            existing.SetPrice(request.Basis, request.FixedPrice, request.MarginPercent);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PriceLineSummary(
            existing.Id,
            request.OwnerId,
            item.Id,
            item.Name,
            existing.Basis,
            existing.FixedPrice,
            existing.MarginPercent);
    }

    /// <inheritdoc />
    public async Task<PriceLineSummary> SetCustomerSpecialPriceAsync(
        SavePriceLineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await RequireCustomerAsync(companyId, request.OwnerId, cancellationToken).ConfigureAwait(false);
        var item = await RequireActiveItemAsync(companyId, request.ItemId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await FindSpecialLineAsync(request.OwnerId, request.ItemId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new CustomerSpecialPrice(
                companyId,
                request.OwnerId,
                item.Id,
                request.Basis,
                request.FixedPrice,
                request.MarginPercent);

            await _unitOfWork.Repository<CustomerSpecialPrice>()
                .AddAsync(existing, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            existing.SetPrice(request.Basis, request.FixedPrice, request.MarginPercent);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PriceLineSummary(
            existing.Id,
            request.OwnerId,
            item.Id,
            item.Name,
            existing.Basis,
            existing.FixedPrice,
            existing.MarginPercent);
    }

    /// <inheritdoc />
    public async Task RemoveCategoryPriceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var line = await _unitOfWork.Repository<CategoryPriceListLine>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (line is null || line.CompanyId != companyId)
        {
            throw new InvalidOperationException("This price-list line does not exist.");
        }

        _unitOfWork.Repository<CategoryPriceListLine>().Remove(line);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveCustomerSpecialPriceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var line = await _unitOfWork.Repository<CustomerSpecialPrice>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (line is null || line.CompanyId != companyId)
        {
            throw new InvalidOperationException("This special price does not exist.");
        }

        _unitOfWork.Repository<CustomerSpecialPrice>().Remove(line);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PriceLineSummary>> ToSummariesAsync(
        Guid companyId,
        IEnumerable<(Guid Id, Guid OwnerId, Guid ItemId, PriceBasis Basis, decimal? FixedPrice,
            decimal? MarginPercent)> lines,
        CancellationToken cancellationToken)
    {
        var itemsById = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        return lines
            .Select(line => new PriceLineSummary(
                line.Id,
                line.OwnerId,
                line.ItemId,
                itemsById.GetValueOrDefault(line.ItemId, "?"),
                line.Basis,
                line.FixedPrice,
                line.MarginPercent))
            .OrderBy(summary => summary.ItemName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<CategoryPriceListLine?> FindCategoryLineAsync(
        Guid categoryId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CategoryPriceListLine>()
                .ListAsync(new LinesOfCategoryAndItem(categoryId, itemId), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

    private async Task<CustomerSpecialPrice?> FindSpecialLineAsync(
        Guid customerId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CustomerSpecialPrice>()
                .ListAsync(new LinesOfCustomerAndItem(customerId, itemId), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

    private async Task<CustomerCategory> RequireCategoryAsync(
        Guid companyId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await _unitOfWork.Repository<CustomerCategory>()
            .GetByIdAsync(categoryId, cancellationToken)
            .ConfigureAwait(false);

        if (category is null || category.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                "This customer category does not exist in this company's books.");
        }

        return category;
    }

    private async Task<Customer> RequireCustomerAsync(
        Guid companyId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await _unitOfWork.Repository<Customer>()
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null || customer.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                "This customer does not exist in this company's books.");
        }

        return customer;
    }

    private async Task<Item> RequireActiveItemAsync(
        Guid companyId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<Item>()
            .GetByIdAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.CompanyId != companyId)
        {
            throw new InvalidOperationException("This item does not exist in this company's books.");
        }

        if (!item.IsActive)
        {
            throw new InvalidOperationException(
                $"'{item.Name}' is inactive; it cannot take a new price line.");
        }

        return item;
    }

    private sealed class CategoryLines : Specification<CategoryPriceListLine>
    {
        public CategoryLines(Guid categoryId)
        {
            Where(line => line.CustomerCategoryId == categoryId);
            ApplyNoTracking();
        }
    }

    private sealed class CustomerLines : Specification<CustomerSpecialPrice>
    {
        public CustomerLines(Guid customerId)
        {
            Where(line => line.CustomerId == customerId);
            ApplyNoTracking();
        }
    }

    private sealed class LinesOfCategoryAndItem : Specification<CategoryPriceListLine>
    {
        public LinesOfCategoryAndItem(Guid categoryId, Guid itemId)
        {
            // Tracked on purpose: an existing line is mutated through this read.
            Where(line => line.CustomerCategoryId == categoryId && line.ItemId == itemId);
        }
    }

    private sealed class LinesOfCustomerAndItem : Specification<CustomerSpecialPrice>
    {
        public LinesOfCustomerAndItem(Guid customerId, Guid itemId)
        {
            // Tracked on purpose: an existing line is mutated through this read.
            Where(line => line.CustomerId == customerId && line.ItemId == itemId);
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
}
