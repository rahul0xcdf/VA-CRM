using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Customers;

/// <summary>
/// The Phase 4 vertical slice exercised end to end against a real SQLite file: the seeded category
/// price resolving on a real customer (C4.1), the persisted override evidence with its edit-log row
/// (C4.2), the bill-wise allocation of receipts oldest-bill-first (C4.6), the auto-created Sundry
/// Debtors account, the referenced-category guards and the credit policy
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Everything runs through the public application services over the real DI composition, each
/// operation in its own DI scope exactly as the shell would drive it — the same discipline as
/// TradeWorkflowTests, which this sits beside.
/// </remarks>
public sealed class CustomerWorkflowTests
{
    private static readonly DateOnly InFy2026 = new(2026, 8, 20);

    /// <summary>
    /// A host whose "today" sits after every voucher the tests raise: bill-wise and credit checks
    /// read balances as of today, and a fixed clock earlier than the bills would see none of them.
    /// </summary>
    private static SqliteTestHost NewHost() =>
        new(new FixedClock(new DateTimeOffset(2026, 9, 15, 5, 0, 0, TimeSpan.Zero)));

    // ---------------------------------------------------------------- seed + settings

    [Fact]
    public async Task EnsureCustomerSeed_SeedsFourCategoriesAndSettings_AndIsIdempotent()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var categories = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICustomerCategoryService>().ListAsync());

        categories.Select(category => category.Name)
            .ShouldBe(["Retailer", "Dealer", "Wholesaler", "Institutional"], ignoreOrder: true);
        categories.ShouldAllBe(category => category.IsActive);

        var settings = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ISalesSettingsService>().GetAsync());
        settings.AllowPriceOverride.ShouldBeTrue();
        settings.CreditLimitPolicy.ShouldBe(CreditLimitPolicy.Warn);

        // Running the seed again writes nothing: the row-granular contract.
        var categoryCountBefore = await CountAsync<CustomerCategory>(host);
        var settingsCountBefore = await CountAsync<SalesSettings>(host);

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>()
                .EnsureCustomerSeedAsync(world.CompanyId));

        (await CountAsync<CustomerCategory>(host)).ShouldBe(categoryCountBefore);
        (await CountAsync<SalesSettings>(host)).ShouldBe(settingsCountBefore);
    }

    // ---------------------------------------------------------------- C4.1 — category price resolves

    [Fact]
    public async Task ResolveForSale_CustomerInSeededCategory_ResolvesTheCategoryPrice()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Cisno Pump 1 HP");
        var dealer = await CreateCustomerAsync(host, "Ram Motors", world.DealerCategoryId);

        await host.InScopeAsync(sp => sp.GetRequiredService<IPriceListService>()
            .SetCategoryPriceAsync(new SavePriceLineRequest(
                Id: null, world.DealerCategoryId, pump.Id,
                PriceBasis.FixedPrice, FixedPrice: 175m)));

        var resolved = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPricingService>()
                .ResolveForSaleAsync(world.CompanyId, dealer.Id, pump.Id));

        resolved.ShouldNotBeNull();
        resolved.Source.ShouldBe(SalePriceSource.CategoryPriceList);
        resolved.Basis.ShouldBe(PriceBasis.FixedPrice);
        resolved.RateMinor.ShouldBe(1_750_000L);
    }

    // ---------------------------------------------------------------- C4.2 — override evidence

    [Fact]
    public async Task OverrideBeforeSave_PersistsAppliedRateAndEvidence_WithAnEditLogRow()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Openwell Pump");
        var dealer = await CreateCustomerAsync(host, "Sagar Traders", world.DealerCategoryId);

        await host.InScopeAsync(sp => sp.GetRequiredService<IPriceListService>()
            .SetCategoryPriceAsync(new SavePriceLineRequest(
                Id: null, world.DealerCategoryId, pump.Id,
                PriceBasis.FixedPrice, FixedPrice: 250m)));

        var resolved = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPricingService>()
                .ResolveForSaleAsync(world.CompanyId, dealer.Id, pump.Id));
        resolved.ShouldNotBeNull();

        // The operator accepts the auto-applied price's evidence but keys 200 over the applied 250
        // before the first save — exactly the case an audit before-image alone could not show.
        var draft = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.TaxInvoice,
            InFy2026,
            dealer.LedgerId,
            [new TradeDocumentLineInput(
                pump.Id, world.MainGodownId, 2m, 200m,
                AppliedRateMinor: resolved.RateMinor,
                IsPriceOverridden: true,
                PriceSource: SalePriceSource.CategoryPriceList)])));

        var line = await host.InScopeAsync(async sp =>
        {
            var lines = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<TradeDocumentLine>()
                .ListAsync(new LinesOfDocument(draft.Id));
            return lines.Single();
        });

        line.AppliedRateMinor.ShouldBe(resolved.RateMinor);
        line.RateMinor.ShouldBe(2_000_000L);
        line.AppliedRateMinor.ShouldNotBe(line.RateMinor);
        line.IsPriceOverridden.ShouldBeTrue();
        line.PriceSource.ShouldBe(SalePriceSource.CategoryPriceList);

        // And the edit log carries the after-image with both fields — the evidence C4.2 reads.
        var afterJson = await host.InScopeAsync(async sp =>
        {
            var auditRows = await AuditLogAccess.ForEntityAsync(sp, "TradeDocumentLine");
            return auditRows.Single(row => row.EntityId == line.Id.ToString()).AfterJson;
        });

        afterJson.ShouldNotBeNull();
        afterJson.ShouldContain("\"AppliedRateMinor\":2500000");
        afterJson.ShouldContain("\"IsPriceOverridden\":true");
        afterJson.ShouldContain("\"PriceSource\":\"CategoryPriceList\"");
    }

    // ---------------------------------------------------------------- C4.6 — bill-wise allocation

    [Fact]
    public async Task BillwiseOutstanding_SixHundredReceipt_GoesToTheOldestBillFirst()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Monoblock Pump");
        var dealer = await CreateCustomerAsync(host, "Anand Traders", world.DealerCategoryId);
        await ReceiveStockAsync(host, world, pump.Id, quantity: 10m, cost: 150m);

        // Older bill 1_000 (4 × 250), newer bill 400 (2 × 200), posted in that order.
        var older = await PostInvoiceAsync(host, world, dealer, pump.Id, InFy2026, quantity: 4m, rate: 250m);
        var newer = await PostInvoiceAsync(
            host, world, dealer, pump.Id, InFy2026.AddDays(3), quantity: 2m, rate: 200m);

        // One 600 receipt lands on the account.
        await PostReceiptAsync(host, world, dealer.LedgerId, InFy2026.AddDays(5), 600m);

        var open = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICustomerStatementService>()
                .BillwiseOutstandingAsync(dealer.Id));

        open.Count.ShouldBe(2);

        var oldest = open.Single(row => row.InvoiceId == older);
        oldest.BilledAmount.ShouldBe(1000m);
        oldest.PaidAmount.ShouldBe(600m);
        oldest.OutstandingAmount.ShouldBe(400m);

        var newest = open.Single(row => row.InvoiceId == newer);
        newest.BilledAmount.ShouldBe(400m);
        newest.PaidAmount.ShouldBe(0m);
        newest.OutstandingAmount.ShouldBe(400m);

        // The statement agrees with the bills: opening 0, closing 800 (checkpoint C4.3's shape).
        var statement = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICustomerStatementService>()
                .CustomerStatementAsync(dealer.Id, InFy2026, InFy2026.AddDays(30)));
        statement.ShouldNotBeNull();
        statement.OpeningBalance.ShouldBe(0m);
        statement.ClosingBalance.ShouldBe(800m);
        statement.Rows.Count.ShouldBe(3);
    }

    // ---------------------------------------------------------------- customer + ledger

    [Fact]
    public async Task SaveCustomer_AutoCreatesItsSundryDebtorsLedger()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var saved = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICustomerService>().SaveAsync(new SaveCustomerRequest(
                Id: null, "Bharat Traders", world.RetailerCategoryId, CreditLimit: 50_000m)));

        saved.CreatedLedger.ShouldBeTrue();

        var ledger = await host.InScopeAsync(async sp =>
            await sp.GetRequiredService<IUnitOfWork>().Repository<Ledger>()
                .GetByIdAsync(saved.LedgerId));

        ledger.ShouldNotBeNull();
        ledger!.Name.ShouldBe("Bharat Traders");

        var group = await host.InScopeAsync(async sp =>
            await sp.GetRequiredService<IUnitOfWork>().Repository<AccountGroup>()
                .GetByIdAsync(ledger.AccountGroupId));
        group.ShouldNotBeNull();
        group!.Name.ShouldBe("Sundry Debtors");

        // Re-categorisation is ordinary maintenance and must be allowed.
        await host.InScopeAsync(sp => sp.GetRequiredService<ICustomerService>().SaveAsync(
            new SaveCustomerRequest(
                saved.Id, "Bharat Traders", world.WholesalerCategoryId, CreditLimit: 50_000m)));

        var recategorised = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICustomerService>().GetAsync(saved.Id));
        recategorised.ShouldNotBeNull();
        recategorised!.CustomerCategoryId.ShouldBe(world.WholesalerCategoryId);
        recategorised.LedgerId.ShouldBe(saved.LedgerId);
    }

    // ---------------------------------------------------------------- category guards

    [Fact]
    public async Task DeactivateOrDelete_CategoryWithCustomers_IsRefused()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await CreateCustomerAsync(host, "Deepak Agencies", world.RetailerCategoryId);

        var deactivateRefusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp.GetRequiredService<ICustomerCategoryService>()
                .DeactivateAsync(world.RetailerCategoryId)));
        deactivateRefusal.Message.ShouldContain("still has customers");

        var deleteRefusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp.GetRequiredService<ICustomerCategoryService>()
                .DeleteAsync(world.RetailerCategoryId)));
        deleteRefusal.Message.ShouldContain("still has customers");

        // An empty category deactivates and deletes freely.
        var spare = await host.InScopeAsync(sp => sp.GetRequiredService<ICustomerCategoryService>()
            .SaveAsync(new SaveCustomerCategoryRequest(null, "Export")));
        await host.InScopeAsync(sp => sp.GetRequiredService<ICustomerCategoryService>()
            .DeactivateAsync(spare.Id));
        await host.InScopeAsync(sp => sp.GetRequiredService<ICustomerCategoryService>()
            .DeleteAsync(spare.Id));
    }

    // ---------------------------------------------------------------- credit policy

    [Fact]
    public async Task CreditCheck_BlockPolicyRejectsOverLimit_WarnAllows()
    {
        await using var host = NewHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var dealer = await CreateCustomerAsync(
            host, "Kiran Traders", world.DealerCategoryId, creditLimit: 1_000m);

        // Warn (the seeded default): over the limit, but never blocked. Minor units: ₹1,000 is
        // 10_000_000; the proposed ₹1,500 is 15_000_000.
        var warned = await host.InScopeAsync(sp => sp.GetRequiredService<ICreditLimitService>()
            .EvaluateAsync(world.CompanyId, dealer.Id, additionalValueMinor: 15_000_000L));
        warned.Policy.ShouldBe(CreditLimitPolicy.Warn);
        warned.IsBlocked.ShouldBeFalse();
        warned.LimitMinor.ShouldBe(10_000_000L);
        warned.CurrentOutstandingMinor.ShouldBe(0L);

        await host.InScopeAsync(sp => sp.GetRequiredService<ISalesSettingsService>()
            .UpdateAsync(new SaveSalesSettingsRequest(
                AllowPriceOverride: true, CreditLimitPolicy.Block)));

        var blocked = await host.InScopeAsync(sp => sp.GetRequiredService<ICreditLimitService>()
            .EvaluateAsync(world.CompanyId, dealer.Id, additionalValueMinor: 15_000_000L));
        blocked.Policy.ShouldBe(CreditLimitPolicy.Block);
        blocked.IsBlocked.ShouldBeTrue();

        var withinLimit = await host.InScopeAsync(sp => sp.GetRequiredService<ICreditLimitService>()
            .EvaluateAsync(world.CompanyId, dealer.Id, additionalValueMinor: 8_000_000L));
        withinLimit.IsBlocked.ShouldBeFalse();

        // A zero limit means no limit at all, under either policy.
        var unlimited = await CreateCustomerAsync(host, "Cash Counter", world.RetailerCategoryId);
        var stillAllowed = await host.InScopeAsync(sp => sp.GetRequiredService<ICreditLimitService>()
            .EvaluateAsync(world.CompanyId, unlimited.Id, additionalValueMinor: 9_999_999L));
        stillAllowed.IsBlocked.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- arrange helpers

    private sealed record CustomerWorld(
        Guid CompanyId,
        Guid MainGodownId,
        Guid CashLedgerId,
        Guid RetailerCategoryId,
        Guid DealerCategoryId,
        Guid WholesalerCategoryId,
        Guid InstitutionalCategoryId);

    private static Task<T> TradeAsync<T>(SqliteTestHost host, Func<ITradeDocumentService, Task<T>> action) =>
        host.InScopeAsync(sp => action(sp.GetRequiredService<ITradeDocumentService>()));

    private static Task<int> CountAsync<TEntity>(SqliteTestHost host)
        where TEntity : Entity =>
        host.InScopeAsync(sp => sp.GetRequiredService<IUnitOfWork>().Repository<TEntity>().CountAsync());

    private static async Task<CustomerWorld> ArrangeAsync(SqliteTestHost host)
    {
        var companyId = await AddFullyProvisionedCompanyAsync(host);

        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var godown = (await unitOfWork.Repository<Domain.Inventory.Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);
            var cash = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Name == "Cash");

            var categories = (await unitOfWork.Repository<CustomerCategory>().ListAsync())
                .Where(category => category.CompanyId == companyId)
                .ToDictionary(category => category.Name, category => category.Id);

            return new CustomerWorld(
                companyId,
                godown.Id,
                cash.Id,
                categories["Retailer"],
                categories["Dealer"],
                categories["Wholesaler"],
                categories["Institutional"]);
        });
    }

    private static async Task<Guid> AddFullyProvisionedCompanyAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().ProvisionAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureInventorySeedAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsurePipelineSeedAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureCustomerSeedAsync(companyId));

        return companyId;
    }

    private sealed record ItemProxy(Guid Id, string Name);

    private static Task<ItemProxy> CreateItemAsync(SqliteTestHost host, string name) =>
        host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IInventoryMasterService>();
            var unit = (await masters.ListUnitsAsync()).Single(unit => unit.Code == "NOS");

            var saved = await masters.SaveItemAsync(new SaveItemRequest(Id: null, name, unit.Id));

            return new ItemProxy(saved.Id, saved.Name);
        });

    private sealed record CustomerProxy(Guid Id, Guid LedgerId);

    private static Task<CustomerProxy> CreateCustomerAsync(
        SqliteTestHost host,
        string name,
        Guid categoryId,
        decimal creditLimit = 0m) =>
        host.InScopeAsync(async sp =>
        {
            var saved = await sp.GetRequiredService<ICustomerService>().SaveAsync(
                new SaveCustomerRequest(Id: null, name, categoryId, CreditLimit: creditLimit));

            return new CustomerProxy(saved.Id, saved.LedgerId);
        });

    private static Task<PostedStockVoucherSummary> ReceiveStockAsync(
        SqliteTestHost host,
        CustomerWorld world,
        Guid itemId,
        decimal quantity,
        decimal cost) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        itemId,
                        world.MainGodownId,
                        quantity,
                        UnitCost: cost),
                ],
                Narration: "opening purchases",
                CounterLedgerId: world.CashLedgerId)));

    private static async Task<Guid> PostInvoiceAsync(
        SqliteTestHost host,
        CustomerWorld world,
        CustomerProxy customer,
        Guid itemId,
        DateOnly date,
        decimal quantity,
        decimal rate)
    {
        var godownId = world.MainGodownId;

        var draft = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.TaxInvoice,
            date,
            customer.LedgerId,
            [new TradeDocumentLineInput(itemId, godownId, quantity, rate)])));

        var posted = await TradeAsync(host, trade => trade.PostInvoiceAsync(draft.Id));

        return posted.DocumentId;
    }

    /// <summary>Posts a Phase 1 receipt: cash in, the customer's account credited.</summary>
    private static Task<PostedVoucherSummary> PostReceiptAsync(
        SqliteTestHost host,
        CustomerWorld world,
        Guid customerLedgerId,
        DateOnly date,
        decimal amount) =>
        host.InScopeAsync(async sp =>
        {
            var posting = sp.GetRequiredService<IVoucherPostingService>();

            var receiptType = (await posting.ListVoucherTypesAsync())
                .Single(candidate => candidate.Name == "Receipt");

            return await posting.PostAsync(new PostVoucherRequest(
                receiptType.Id,
                date,
                [
                    new PostVoucherLine(world.CashLedgerId, amount, 0m, Narration: "cash received"),
                    new PostVoucherLine(customerLedgerId, 0m, amount, Narration: "against bills"),
                ],
                Narration: "part payment received"));
        });

    private sealed class LinesOfDocument : Specification<TradeDocumentLine>
    {
        public LinesOfDocument(Guid documentId)
        {
            Where(line => line.TradeDocumentId == documentId);
            ApplyNoTracking();
        }
    }
}
