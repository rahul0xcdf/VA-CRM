using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// Builds the smallest set of books a Phase 6 test needs: a provisioned company with a structured
/// address, a registered B2B customer, an item with an HSN code and a rate, and one posted tax
/// invoice (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// Shared between the e-invoice and e-way bill workflow tests because both need the same books and
/// neither is interesting until an invoice is actually posted. The arrangement goes through the real
/// services — provisioning, the stock voucher path, the trade document path — so the tax snapshot the
/// e-invoice sends is the one Phase 5 genuinely posted, rather than a figure a test invented.
/// </remarks>
internal static class EinvoiceTestWorld
{
    public const string CompanyGstin = "29ABCDE1234F1Z5";
    public const string CustomerGstin = "27AAPFU0939F1ZV";
    public const string ClientId = "nic-client-id-value";
    public const string ClientSecret = "nic-client-secret-value";
    public const string PortalUsername = "nic-portal-username";
    public const string PortalPassword = "nic-portal-password";

    public static readonly DateOnly InvoiceDate = new(2026, 8, 20);

    /// <summary>The four values checkpoint C6.6 scans for.</summary>
    public static string[] AllSecrets => [ClientId, ClientSecret, PortalUsername, PortalPassword];

    /// <summary>
    /// A host with the two Phase 6 ports substituted: the portal, because there are no sandbox
    /// credentials (<c>open-questions.md</c> #10), and the protector, because DPAPI ties ciphertext to
    /// one Windows user and these tests must pass anywhere.
    /// </summary>
    public static SqliteTestHost NewHost(RecordedIrpServer portal) =>
        new(irpClient: portal, credentialProtector: new ReversibleCredentialProtector());

    public static async Task<EinvoiceWorld> ArrangeAsync(
        SqliteTestHost host,
        bool withCredentials = true)
    {
        var companyId = await ProvisionCompanyAsync(host);

        var world = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var chartOfAccounts = sp.GetRequiredService<IChartOfAccountsService>();
            var hsnSacService = sp.GetRequiredService<IHsnSacService>();
            var rateService = sp.GetRequiredService<IGstRateService>();

            var company = await unitOfWork.Repository<Company>().GetByIdAsync(companyId);
            company!.SetGstin(CompanyGstin);

            // The structured address the IRP needs (phase-6 §4b).
            company.SetAddress("14 Market Road", "Near Bus Stand", "Mysuru", 570001);
            unitOfWork.Repository<Company>().Update(company);
            await unitOfWork.SaveChangesAsync();

            var godown = (await unitOfWork.Repository<Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);

            var groups = await unitOfWork.Repository<AccountGroup>().ListAsync();
            var debtors = groups.Single(g => g.CompanyId == companyId && g.Name == "Sundry Debtors");
            var creditors = groups.Single(g => g.CompanyId == companyId && g.Name == "Sundry Creditors");

            var customerLedger = await chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest("Ram Motors", debtors.Id));
            var supplierLedger = await chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest("Cisno Pumps Ltd", creditors.Id));

            var category = (await unitOfWork.Repository<CustomerCategory>().ListAsync())
                .First(candidate => candidate.CompanyId == companyId);

            // Registered in Maharashtra, so the invoice is inter-state and carries IGST — which also
            // makes the B2B path the one under test.
            var customer = new Customer(
                companyId,
                "Ram Motors",
                customerLedger.Id,
                category.Id,
                gstin: CustomerGstin,
                placeOfSupplyStateCode: "27");

            customer.SetAddresses("Plot 9, Industrial Estate", null);
            customer.SetCityAndPincode("Pune", 411001);

            await unitOfWork.Repository<Customer>().AddAsync(customer);
            await unitOfWork.SaveChangesAsync();

            var code = await hsnSacService.SaveAsync(
                new SaveHsnSacRequest(Id: null, Code: "84137010", Description: "Centrifugal pumps"));
            await rateService.AddAsync(new SaveGstRateScheduleRequest(
                code.Id, 180_000L, EffectiveFrom: new DateOnly(2026, 4, 1)));

            var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Code == "NOS");

            var item = new Item(companyId, "Openwell Pump", unit.Id);
            item.AssignHsnSac(code.Id);
            await unitOfWork.Repository<Item>().AddAsync(item);
            await unitOfWork.SaveChangesAsync();

            return new EinvoiceWorld(
                companyId,
                customerLedger.Id,
                supplierLedger.Id,
                godown.Id,
                item.Id,
                Guid.Empty,
                string.Empty);
        });

        if (withCredentials)
        {
            await host.InScopeAsync(sp =>
                sp.GetRequiredService<IEinvoiceSettingsService>().SaveCredentialsAsync(
                    new SaveIrpCredentialsRequest(
                        ClientId, ClientSecret, PortalUsername, PortalPassword)));
        }

        return await PostInvoiceAsync(host, world);
    }

    private static async Task<Guid> ProvisionCompanyAsync(SqliteTestHost host)
    {
        // The schema is created exactly as the application creates it at startup, rather than by a
        // test-only EnsureCreated: a Phase 6 table the migration forgot must fail here.
        await host.MigrateAsync();

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
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureGstSeedAsync(companyId));

        return companyId;
    }

    /// <summary>Receives stock, then posts one ₹1,000 B2B tax invoice against it.</summary>
    private static Task<EinvoiceWorld> PostInvoiceAsync(SqliteTestHost host, EinvoiceWorld world) =>
        host.InScopeAsync(async sp =>
        {
            var trade = sp.GetRequiredService<ITradeDocumentService>();
            var stock = sp.GetRequiredService<IStockVoucherService>();

            await stock.PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                new DateOnly(2026, 8, 1),
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        world.ItemId,
                        world.GodownId,
                        10m,
                        UnitCost: 100m),
                ],
                Narration: "opening stock",
                CounterLedgerId: world.SupplierLedgerId));

            var draft = await trade.CreateAsync(new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                InvoiceDate,
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(world.ItemId, world.GodownId, 2m, 500m)]));

            var posted = await trade.PostInvoiceAsync(draft.Id);

            return world with { InvoiceId = posted.DocumentId, InvoiceNumber = posted.DocumentNumber };
        });
}

/// <summary>The identifiers a Phase 6 test needs out of the arranged books.</summary>
internal sealed record EinvoiceWorld(
    Guid CompanyId,
    Guid CustomerLedgerId,
    Guid SupplierLedgerId,
    Guid GodownId,
    Guid ItemId,
    Guid InvoiceId,
    string InvoiceNumber);
