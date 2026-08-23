namespace VinayakaAgencies.Application.Customers;

/// <summary>Read model for one customer category.</summary>
/// <param name="CustomerCount">How many customers currently sit in the category.</param>
public sealed record CustomerCategorySummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int CustomerCount);

/// <param name="Id">Null creates a new category; a value renames and re-describes that one.</param>
public sealed record SaveCustomerCategoryRequest(
    Guid? Id,
    string Name,
    string? Description = null);

/// <summary>
/// Maintains the customer categories whose price lists price their customers
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Categories are master data, not an enum (§4a): freely renamable and extendable. A category with
/// customers or live price lines cannot be deleted — it is deactivated instead, exactly like every
/// other master the books reference.
/// </remarks>
public interface ICustomerCategoryService
{
    Task<IReadOnlyList<CustomerCategorySummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<CustomerCategorySummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a category, or updates the named one when <c>request.Id</c> is set.</summary>
    Task<CustomerCategorySummary> SaveAsync(
        SaveCustomerCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a category nothing references. One with customers or price-list lines is refused —
    /// deactivate it instead.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
