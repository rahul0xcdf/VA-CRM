using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// Maintains the customer master, creating each customer's Sundry Debtors account alongside them
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// The 1:1 ledger is created inside the caller's save — both rows staged through the unit of work,
/// one transaction, so a customer can never exist without their account. Deactivation touches only
/// the customer: their ledger stays open, because money is still owed into it and receipts must
/// keep posting — the same reason Phase 1 refuses to delete a ledger with journal lines.
/// </remarks>
public sealed class CustomerService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerSummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var customers = await LoadCustomersAsync(companyId, cancellationToken).ConfigureAwait(false);

        return customers
            .Where(customer => includeInactive || customer.Customer.IsActive)
            .OrderBy(customer => customer.Customer.NameNormalised, StringComparer.Ordinal)
            .Select(customer => ToSummary(customer.Customer, customer.Ledger, customer.Category))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerSummary>> SearchAsync(
        string searchText,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);

        // Substring over the normalised forms: case and spacing stop mattering the moment the
        // operator types, which is what an entry screen owes them.
        var needle = NameNormaliser.Normalise(searchText);

        return (await ListAsync(includeInactive, cancellationToken).ConfigureAwait(false))
            .Where(summary => NameNormaliser.Normalise(summary.Name)
                .Contains(needle, StringComparison.Ordinal))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CustomerSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var world = await RequireWorldAsync(companyId, id, cancellationToken).ConfigureAwait(false);

        return ToSummary(world.Customer, world.Ledger, world.Category);
    }

    /// <inheritdoc />
    public async Task<SavedCustomer> SaveAsync(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var category = await RequireCategoryAsync(
                companyId, request.CustomerCategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (!category.IsActive)
        {
            throw new InvalidOperationException(
                $"Category '{category.Name}' is inactive; activate it before placing customers in it.");
        }

        Ledger? createdLedger = null;

        if (request.Id.HasValue)
        {
            var world = await RequireWorldAsync(companyId, request.Id.Value, cancellationToken)
                .ConfigureAwait(false);

            var customer = world.Customer;

            if (!string.Equals(
                    customer.NameNormalised,
                    NameNormaliser.Normalise(request.Name),
                    StringComparison.Ordinal))
            {
                await RequireNoLedgerNameClashAsync(companyId, request.Name, cancellationToken)
                    .ConfigureAwait(false);
            }

            customer.Rename(request.Name);
            customer.MoveToCategory(request.CustomerCategoryId);
            customer.SetContact(request.ContactPerson, request.PhoneNumber, request.Email);
            customer.SetGstin(request.Gstin);
            customer.SetPlaceOfSupplyStateCode(request.PlaceOfSupplyStateCode);
            customer.SetAddresses(request.BillingAddressLines, request.ShippingAddressLines);
            customer.SetCreditTerms(request.CreditLimit, request.CreditPeriodDays);

            if (request.IsActive)
            {
                customer.Activate();
            }
            else
            {
                customer.Deactivate();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new SavedCustomer(
                customer.Id, customer.Name, customer.LedgerId, category.Id, CreatedLedger: false);
        }

        await RequireNoLedgerNameClashAsync(companyId, request.Name, cancellationToken)
            .ConfigureAwait(false);

        var group = await RequireDebtorsGroupAsync(companyId, cancellationToken).ConfigureAwait(false);

        // The account and the master land together or not at all: one staged pair, one save.
        createdLedger = new Ledger(companyId, request.Name, group.Id);
        var newCustomer = new Customer(
            companyId,
            request.Name,
            createdLedger.Id,
            category.Id,
            request.ContactPerson,
            request.PhoneNumber,
            request.Email,
            request.Gstin,
            request.PlaceOfSupplyStateCode,
            request.BillingAddressLines,
            request.ShippingAddressLines,
            request.CreditLimit,
            request.CreditPeriodDays);

        await _unitOfWork.Repository<Ledger>().AddAsync(createdLedger, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.Repository<Customer>().AddAsync(newCustomer, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SavedCustomer(
            newCustomer.Id, newCustomer.Name, createdLedger.Id, category.Id, CreatedLedger: true);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireWorldAsync(companyId, id, cancellationToken).ConfigureAwait(false))
            .Customer
            .Deactivate();

        // Deliberately nothing on the ledger: the account still carries receivables and must stay
        // open for receipts, exactly as a Phase 3 document keeps posting to its party ledger.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireWorldAsync(companyId, id, cancellationToken).ConfigureAwait(false))
            .Customer
            .Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CustomerSummary ToSummary(Customer customer, Ledger ledger, CustomerCategory category) =>
        new(
            customer.Id,
            customer.Name,
            customer.LedgerId,
            ledger.Name,
            customer.CustomerCategoryId,
            category.Name,
            customer.ContactPerson,
            customer.PhoneNumber,
            customer.Email,
            customer.Gstin,
            customer.PlaceOfSupplyStateCode,
            customer.BillingAddressLines,
            customer.ShippingAddressLines,
            customer.CreditLimit,
            customer.CreditPeriodDays,
            customer.IsActive);

    private async Task<IReadOnlyList<(Customer Customer, Ledger Ledger, CustomerCategory Category)>>
        LoadCustomersAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var customers = await _unitOfWork.Repository<Customer>()
            .ListAsync(new CustomersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (customers.Count == 0)
        {
            return [];
        }

        var ledgersById = (await _unitOfWork.Repository<Ledger>()
                .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(ledger => ledger.Id);

        var categoriesById = (await _unitOfWork.Repository<CustomerCategory>()
                .ListAsync(new CategoriesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(category => category.Id);

        return customers
            .Select(customer => (
                Customer: customer,
                Ledger: ledgersById.GetValueOrDefault(customer.LedgerId)!,
                Category: categoriesById.GetValueOrDefault(customer.CustomerCategoryId)!))
            .Where(row => row.Ledger is not null && row.Category is not null)
            .ToList();
    }

    private async Task<(Customer Customer, Ledger Ledger, CustomerCategory Category)> RequireWorldAsync(
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

        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(customer.LedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null)
        {
            throw new InvalidOperationException(
                $"Customer '{customer.Name}' has no account ledger, which should be impossible "
                + "outside corrupted books.");
        }

        var category = await RequireCategoryAsync(
                companyId, customer.CustomerCategoryId, cancellationToken)
            .ConfigureAwait(false);

        return (customer, ledger, category);
    }

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

    private async Task<AccountGroup> RequireDebtorsGroupAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(CustomerSeedData.SundryDebtorsGroupName);

        var group = await _unitOfWork.Repository<AccountGroup>()
            .SingleOrDefaultAsync(new GroupByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        return group ?? throw new InvalidOperationException(
            $"The '{CustomerSeedData.SundryDebtorsGroupName}' account group is missing from this "
            + "company's chart of accounts, so a new customer has nowhere to hang. Provision the "
            + "company first.");
    }

    /// <summary>
    /// The customer's name becomes their ledger's name, and the ledger table enforces unique names
    /// per company — so a clash here would fail the save halfway through the pair.
    /// </summary>
    private async Task RequireNoLedgerNameClashAsync(
        Guid companyId,
        string name,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(name);

        var clash = await _unitOfWork.Repository<Ledger>()
            .AnyAsync(new LedgerByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (clash)
        {
            throw new InvalidOperationException(
                $"A ledger named '{name}' already exists; choose another name for the customer.");
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

    private sealed class CategoriesOfCompany : Specification<CustomerCategory>
    {
        public CategoriesOfCompany(Guid companyId)
        {
            Where(category => category.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class LedgersOfCompany : Specification<Ledger>
    {
        public LedgersOfCompany(Guid companyId)
        {
            Where(ledger => ledger.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class GroupByName : Specification<AccountGroup>
    {
        public GroupByName(Guid companyId, string nameNormalised)
        {
            Where(group => group.CompanyId == companyId && group.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class LedgerByName : Specification<Ledger>
    {
        public LedgerByName(Guid companyId, string nameNormalised)
        {
            Where(ledger => ledger.CompanyId == companyId && ledger.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }
}
