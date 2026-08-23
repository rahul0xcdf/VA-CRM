using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Trade;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Printing;

/// <summary>
/// Template data end to end against a real SQLite file: the designer's save-without-rebuild path
/// (C3.2), versioned history that appends and never edits, and the default-selection rule over the
/// two seeded tax-invoice templates (C3.6) (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
public sealed class PrintTemplateWorkflowTests
{
    [Fact]
    public async Task PipelineSeed_CreatesTwoTaxInvoiceTemplates_LowestActivePriorityIsDefault()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddFullyProvisionedCompanyAsync(host);

        var templates = await TemplatesAsync(host);

        var taxInvoiceTemplates = templates.Where(row => row.DocumentKind == TradeDocumentKind.TaxInvoice).ToList();
        taxInvoiceTemplates.Count.ShouldBe(2);

        // Listed by ascending priority, as every picker shows them.
        taxInvoiceTemplates.Select(row => row.Name).ShouldBe(
            ["Tax Invoice — Detailed", "Tax Invoice — Compact"]);

        // The rule itself: among active templates, the lowest priority prints by default.
        var defaultTemplate = await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .ResolveDefaultAsync(TradeDocumentKind.TaxInvoice));
        defaultTemplate!.Name.ShouldBe("Tax Invoice — Detailed");
    }

    [Fact]
    public async Task SetActive_RetiringTheDefault_FallsBackToNextPriority()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddFullyProvisionedCompanyAsync(host);

        var templates = await TemplatesAsync(host);
        var detailed = templates.Single(row => row.Name == "Tax Invoice — Detailed");

        await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .SetActiveAsync(detailed.Id, isActive: false));

        var fallback = await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .ResolveDefaultAsync(TradeDocumentKind.TaxInvoice));
        fallback!.Name.ShouldBe("Tax Invoice — Compact");

        // Bringing it back restores the original order.
        await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .SetActiveAsync(detailed.Id, isActive: true));

        (await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
                .ResolveDefaultAsync(TradeDocumentKind.TaxInvoice)))
            !.Name.ShouldBe("Tax Invoice — Detailed");
    }

    [Fact]
    public async Task SetActive_RetiringTheLastActiveTemplateOfAKind_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddFullyProvisionedCompanyAsync(host);

        var templates = await TemplatesAsync(host);
        var compact = templates.Single(row => row.Name == "Tax Invoice — Compact");
        var detailed = templates.Single(row => row.Name == "Tax Invoice — Detailed");

        await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .SetActiveAsync(detailed.Id, isActive: false));

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
                .SetActiveAsync(compact.Id, isActive: false)));

        refusal.Message.ShouldContain("last active template");
    }

    [Fact]
    public async Task SaveLayout_AppendsANewVersionAndHistoryStaysReadable()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddFullyProvisionedCompanyAsync(host);

        var templates = await TemplatesAsync(host);
        var detailedId = templates.Single(row => row.Name == "Tax Invoice — Detailed").Id;

        var before = (await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .GetAsync(detailedId)))!;

        // The operator changes what the document looks like — title, logo switch, terms — and
        // saves. No rebuild, no developer (checkpoint C3.2).
        var updatedLayout = before.Layout with
        {
            Title = "VINAYAKA AGENCIES — TAX INVOICE",
            ShowLogo = true,
            LogoPath = @"C:\Stationery\logo.png",
            TermsAndConditions = "Goods once sold will not be taken back.",
            VisibleColumns = ["Sr", "Item", "Qty", "Rate", "Amount"],
        };

        var savedVersionTwo = await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .SaveLayoutAsync(detailedId, updatedLayout));

        savedVersionTwo.CurrentVersion.ShouldBe(before.CurrentVersion + 1);
        savedVersionTwo.Revisions.Count.ShouldBe(before.Revisions.Count + 1);
        savedVersionTwo.Layout.Title.ShouldBe("VINAYAKA AGENCIES — TAX INVOICE");

        // Reading back in a fresh scope returns the saved layout at the new version, with every
        // earlier revision still listed for the audit.
        var reread = await host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>()
            .GetAsync(detailedId));

        reread!.CurrentVersion.ShouldBe(savedVersionTwo.CurrentVersion);
        reread.Layout.ShowLogo.ShouldBeTrue();
        reread.Layout.LogoPath.ShouldBe(@"C:\Stationery\logo.png");
        reread.Layout.TermsAndConditions.ShouldBe("Goods once sold will not be taken back.");
        reread.Revisions.Select(revision => revision.Version)
            .ShouldBe(reread.Revisions.Select(revision => revision.Version).OrderByDescending(version => version));
    }

    [Fact]
    public void LayoutJson_RoundTripsThroughTheOneSerialiser()
    {
        var layout = new PrintTemplateLayout(
            Title: "Quotation",
            ShowLogo: false,
            LogoPath: null,
            HeaderLines: ["12 Market Road, Jamakhandi", "GSTIN: 29AAAAA0000A1Z5"],
            FooterNote: "Subject to Jamakhandi jurisdiction",
            TermsAndConditions: null,
            VisibleColumns: ["Item", "Qty", "Rate", "Amount"],
            CopyLabels: []);

        var deserialised = PrintTemplateService.Deserialise(PrintTemplateService.Serialise(layout));

        // Field by field: record equality on collection properties is reference equality, which a
        // serialise round trip can never satisfy.
        deserialised.Title.ShouldBe(layout.Title);
        deserialised.ShowLogo.ShouldBe(layout.ShowLogo);
        deserialised.LogoPath.ShouldBe(layout.LogoPath);
        deserialised.HeaderLines.ShouldBe(layout.HeaderLines);
        deserialised.FooterNote.ShouldBe(layout.FooterNote);
        deserialised.TermsAndConditions.ShouldBe(layout.TermsAndConditions);
        deserialised.VisibleColumns.ShouldBe(layout.VisibleColumns);
        deserialised.CopyLabels.ShouldBe(layout.CopyLabels);
    }

    // ---------------------------------------------------------------- helpers

    private static Task<IReadOnlyList<PrintTemplateSummary>> TemplatesAsync(SqliteTestHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IPrintTemplateService>().ListAsync());

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

        return companyId;
    }
}
