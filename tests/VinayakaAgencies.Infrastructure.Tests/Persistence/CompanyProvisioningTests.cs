using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// Runtime company provisioning: the §4.3 primary groups, §4.4 sub-groups, §4.5 seed ledgers and
/// §7.1 voucher types with their §7.2 numbering series, written through <see cref="IUnitOfWork"/>
/// against a real SQLite file.
/// </summary>
/// <remarks>
/// The expected values in this file are transcribed from the spec tables by hand and deliberately
/// <b>not</b> read from the production seed tables. A test that asserted the code against its own
/// constants would pass just as happily with "Direct Expenses" classified as income; the point of
/// these assertions is that the code agrees with the spec, so the spec has to be restated here.
/// <para>
/// Provisioning is exercised end to end, not through a double, because §5's whole reason for
/// refusing <c>HasData</c> is that seeding must pass through <c>SaveChangesAsync</c> and leave an
/// edit-log row per row created — which only the real database can show.
/// </para>
/// </remarks>
public sealed class CompanyProvisioningTests
{
    /// <summary>Spec §4.3, transcribed: name, nature, whether it affects gross profit.</summary>
    private static readonly (string Name, AccountNature Nature, bool AffectsGrossProfit)[] PrimaryGroups =
    [
        ("Branch / Divisions", AccountNature.Liability, false),
        ("Capital Account", AccountNature.Liability, false),
        ("Current Assets", AccountNature.Asset, false),
        ("Current Liabilities", AccountNature.Liability, false),
        ("Direct Expenses", AccountNature.Expense, true),
        ("Direct Incomes", AccountNature.Income, true),
        ("Fixed Assets", AccountNature.Asset, false),
        ("Indirect Expenses", AccountNature.Expense, false),
        ("Indirect Incomes", AccountNature.Income, false),
        ("Investments", AccountNature.Asset, false),
        ("Loans (Liability)", AccountNature.Liability, false),
        ("Misc. Expenses (ASSET)", AccountNature.Asset, false),
        ("Purchase Accounts", AccountNature.Expense, true),
        ("Sales Accounts", AccountNature.Income, true),
        ("Suspense A/c", AccountNature.Liability, false),
    ];

    /// <summary>Spec §4.4, transcribed: sub-group and the parent that table names.</summary>
    private static readonly (string Name, string Parent)[] SubGroups =
    [
        ("Bank Accounts", "Current Assets"),
        ("Bank OD A/c", "Loans (Liability)"),
        ("Cash-in-Hand", "Current Assets"),
        ("Deposits (Asset)", "Current Assets"),
        ("Duties & Taxes", "Current Liabilities"),
        ("Loans & Advances (Asset)", "Current Assets"),
        ("Provisions", "Current Liabilities"),
        ("Reserves & Surplus", "Capital Account"),
        ("Secured Loans", "Loans (Liability)"),
        ("Stock-in-Hand", "Current Assets"),
        ("Sundry Creditors", "Current Liabilities"),
        ("Sundry Debtors", "Current Assets"),
        ("Unsecured Loans", "Loans (Liability)"),
    ];

    /// <summary>
    /// Spec §4.5 rows L1–L9 — every ledger marked "Yes" or "Owner decides", the list the owner
    /// approved (open-questions.md #15). L10 bank, L11 party and L12 GST ledgers are absent by
    /// decision and are asserted absent below.
    /// </summary>
    private static readonly string[] SeedLedgerNames =
    [
        "Cash",
        "Profit & Loss A/c",
        "Opening Balance Difference",
        "Stock-in-Hand",
        "Sales",
        "Purchases",
        "Round Off",
        "Discount Allowed",
        "Discount Received",
    ];

    /// <summary>Spec §7.1 and §7.2, transcribed: type name, kind, default series prefix.</summary>
    private static readonly (string Name, VoucherKind Kind, string Prefix)[] VoucherTypes =
    [
        ("Payment", VoucherKind.Payment, "PMT/"),
        ("Receipt", VoucherKind.Receipt, "RCT/"),
        ("Contra", VoucherKind.Contra, "CTR/"),
        ("Journal", VoucherKind.Journal, "JRN/"),
        ("Sales", VoucherKind.Sales, "SAL/"),
        ("Purchase", VoucherKind.Purchase, "PUR/"),
        ("Credit Note", VoucherKind.CreditNote, "CRN/"),
        ("Debit Note", VoucherKind.DebitNote, "DBN/"),
    ];

    private static readonly string[] SeededEntityTypes =
    [
        nameof(AccountGroup),
        nameof(Ledger),
        nameof(VoucherType),
        nameof(VoucherNumberSeries),
    ];

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_CreatesTheFifteenPrimaryGroupsOfTheSpecTable()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var groups = await ReadGroupsAsync(host, companyId);
        var primaries = groups.Where(group => group.IsPrimary).ToList();

        primaries.Count.ShouldBe(15);

        foreach (var (name, nature, affectsGrossProfit) in PrimaryGroups)
        {
            var group = primaries.SingleOrDefault(candidate => candidate.Name == name);

            group.ShouldNotBeNull($"primary group '{name}' (spec §4.3) was not created");
            group.Nature.ShouldBe(nature, $"'{name}' must be {nature} — its nature decides which statement its balances land on");
            group.AffectsGrossProfit.ShouldBe(affectsGrossProfit, $"'{name}' drives the gross-profit line");
            group.ParentGroupId.ShouldBeNull($"'{name}' is a primary group and hangs off nothing");
            group.IsSystem.ShouldBeTrue($"'{name}' is seeded and structural (spec §4.6)");
            group.IsActive.ShouldBeTrue();

            // A primary group's path is just its own id (§4.2).
            group.MaterialisedPath.ShouldBe($"/{group.Id:N}/");
            group.Depth.ShouldBe(1);
        }
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_CreatesTheThirteenSubGroupsUnderTheirNamedParents()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var groups = await ReadGroupsAsync(host, companyId);
        var children = groups.Where(group => !group.IsPrimary).ToList();

        children.Count.ShouldBe(13);
        groups.Count.ShouldBe(28);

        foreach (var (name, parentName) in SubGroups)
        {
            var group = children.SingleOrDefault(candidate => candidate.Name == name);
            group.ShouldNotBeNull($"sub-group '{name}' (spec §4.4) was not created");

            var parent = groups.Single(candidate => candidate.Name == parentName);

            group.ParentGroupId.ShouldBe(parent.Id, $"'{name}' belongs under '{parentName}'");
            group.IsSystem.ShouldBeTrue();
            group.IsPrimary.ShouldBeFalse();

            // Inherited, not chosen: a sub-group whose nature disagreed with its parent would put
            // balances on the wrong statement.
            group.Nature.ShouldBe(parent.Nature);
            group.AffectsGrossProfit.ShouldBe(parent.AffectsGrossProfit);

            group.MaterialisedPath.ShouldBe($"{parent.MaterialisedPath}{group.Id:N}/");
            group.Depth.ShouldBe(2);
        }
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_MaterialisedPathEqualsThePathRecomputedFromParentGroupId()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var groups = await ReadGroupsAsync(host, companyId);
        var byId = groups.ToDictionary(group => group.Id);

        // C1.14's shape, over the seeded tree: the path is a cache of ParentGroupId (§4.2), and the
        // two disagreeing would make every group roll-up quietly wrong rather than loudly broken.
        foreach (var group in groups)
        {
            RecomputePath(group, byId).ShouldBe(
                group.MaterialisedPath,
                $"'{group.Name}' stores a path that its parent chain does not imply");
        }
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_CreatesExactlyTheNineApprovedSeedLedgers()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var ledgers = await ReadLedgersAsync(host, companyId);

        ledgers.Select(ledger => ledger.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(SeedLedgerNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_CreatesNoBankPartyOrTaxLedger()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var groups = await ReadGroupsAsync(host, companyId);
        var ledgers = await ReadLedgersAsync(host, companyId);

        // §4.5 L10–L12 are absent by decision, and the reason differs in each case: a bank ledger
        // needs a real account number and IFSC, party ledgers arrive with the Phase 4 customer
        // master, and a seeded GST ledger would invite someone to hardcode a rate against it in
        // Phase 5 when rates must be data-driven config rows.
        string[] mustBeEmpty =
        [
            "Bank Accounts",
            "Bank OD A/c",
            "Sundry Debtors",
            "Sundry Creditors",
            "Duties & Taxes",
        ];

        foreach (var groupName in mustBeEmpty)
        {
            var group = groups.Single(candidate => candidate.Name == groupName);

            ledgers.Where(ledger => ledger.AccountGroupId == group.Id)
                .Select(ledger => ledger.Name)
                .ShouldBeEmpty($"'{groupName}' must be seeded empty (spec §4.5 L10–L12)");
        }

        string[] forbiddenFragments = ["gst", "cgst", "sgst", "igst", "bank", "debtor", "creditor"];

        foreach (var ledger in ledgers)
        {
            foreach (var fragment in forbiddenFragments)
            {
                ledger.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                    $"seed ledger '{ledger.Name}' looks like a bank, party or tax ledger");
            }
        }
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_LocksProfitAndLossAccountAgainstManualPosting()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var ledgers = await ReadLedgersAsync(host, companyId);
        var profitAndLoss = ledgers.Single(ledger => ledger.Name == "Profit & Loss A/c");

        // §8.4: its balance-sheet figure is computed from income and expense, which is the whole
        // reason it is locked against manual posting — a posting to it would be a stored balance in
        // all but name.
        profitAndLoss.AllowsManualPosting.ShouldBeFalse();
        profitAndLoss.IsSystem.ShouldBeTrue();

        ledgers.Where(ledger => !ledger.AllowsManualPosting)
            .Select(ledger => ledger.Name)
            .ShouldBe(["Profit & Loss A/c"]);

        // Opening Balance Difference is system-locked but stays postable: §8.5 posts the residual of
        // the opening-balance voucher to it.
        var openingDifference = ledgers.Single(ledger => ledger.Name == "Opening Balance Difference");
        openingDifference.IsSystem.ShouldBeTrue();
        openingDifference.AllowsManualPosting.ShouldBeTrue();
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_CreatesEightSystemVoucherTypesWithADefaultSeries()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var (types, series) = await ReadVoucherSetupAsync(host, companyId);

        types.Count.ShouldBe(8);
        series.Count.ShouldBe(8);
        types.Select(type => type.Kind).Distinct().Count().ShouldBe(8);

        foreach (var (name, kind, prefix) in VoucherTypes)
        {
            var type = types.SingleOrDefault(candidate => candidate.Name == name);
            type.ShouldNotBeNull($"voucher type '{name}' (spec §7.1) was not created");

            type.Kind.ShouldBe(kind);
            type.IsSystem.ShouldBeTrue();
            type.IsActive.ShouldBeTrue();

            // DefaultSeriesId carries no foreign key by design (§5), so the two still have to end up
            // mutually consistent: the type points at a series, and that series points back.
            type.DefaultSeriesId.ShouldNotBeNull($"'{name}' has no default numbering series");

            var defaultSeries = series.SingleOrDefault(candidate => candidate.Id == type.DefaultSeriesId);
            defaultSeries.ShouldNotBeNull($"'{name}' points at a series that does not exist");
            defaultSeries.VoucherTypeId.ShouldBe(type.Id);

            defaultSeries.Prefix.ShouldBe(prefix);
            defaultSeries.Width.ShouldBe(4);
            defaultSeries.Suffix.ShouldBeNull();
            defaultSeries.StartNumber.ShouldBe(1L);
            defaultSeries.ResetPolicy.ShouldBe(NumberResetPolicy.FinancialYear);
            defaultSeries.AllowManualOverride.ShouldBeTrue();

            // The number an operator will see on the first voucher of the year (§7.2's example).
            defaultSeries.Format(1).ShouldBe($"{prefix}0001");
        }
    }

    [Fact]
    public async Task ProvisionAsync_ForANewCompany_WritesAnEditLogRowForEveryRowItCreates()
    {
        await using var host = new SqliteTestHost(
            new FixedClock(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero)),
            new StubCurrentUser(@"SHOP-PC\rahul"));

        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        var seeded = rows.Where(row => SeededEntityTypes.Contains(row.EntityType)).ToList();

        // §11.1: every insert of every mapped entity, including — especially — the creation of a
        // system-locked row, which is what an auditor asks about first. 28 groups + 9 ledgers +
        // 8 voucher types + 8 series.
        seeded.Count.ShouldBe(53);
        seeded.Count.ShouldBeGreaterThan(0);

        // Every one a Create. An Update here would mean provisioning wrote a row and then patched
        // it — which is precisely what §5 avoids by leaving DefaultSeriesId without a foreign key.
        seeded.ShouldAllBe(row => row.Action == AuditAction.Create);

        seeded.Count(row => row.EntityType == nameof(AccountGroup)).ShouldBe(28);
        seeded.Count(row => row.EntityType == nameof(Ledger)).ShouldBe(9);
        seeded.Count(row => row.EntityType == nameof(VoucherType)).ShouldBe(8);
        seeded.Count(row => row.EntityType == nameof(VoucherNumberSeries)).ShouldBe(8);

        foreach (var row in seeded)
        {
            row.UserName.ShouldBe(@"SHOP-PC\rahul");
            row.OccurredAtUtc.ShouldBe(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero));
            row.CompanyId.ShouldBe(companyId);
            row.BeforeJson.ShouldBeNull();
            row.AfterJson.ShouldNotBeNullOrWhiteSpace();
        }

        // Spot-check one group's after-image: seeding must not have slipped past the trail with an
        // empty payload.
        var groups = await ReadGroupsAsync(host, companyId);
        var suspense = groups.Single(group => group.Name == "Suspense A/c");

        var suspenseRow = seeded.Single(row =>
            row.EntityType == nameof(AccountGroup)
            && row.EntityId == suspense.Id.ToString("D"));

        using var after = JsonDocument.Parse(suspenseRow.AfterJson!);
        after.RootElement.GetProperty(nameof(AccountGroup.Name)).GetString().ShouldBe("Suspense A/c");
        after.RootElement.GetProperty(nameof(AccountGroup.Nature)).GetString().ShouldBe("Liability");
        after.RootElement.GetProperty(nameof(AccountGroup.IsSystem)).GetBoolean().ShouldBeTrue();
        after.RootElement.GetProperty(nameof(AccountGroup.MaterialisedPath)).GetString()
            .ShouldBe(suspense.MaterialisedPath);
        suspenseRow.ChangedProperties.ShouldContain(nameof(AccountGroup.MaterialisedPath));
    }

    [Fact]
    public async Task ProvisionAsync_CalledTwice_IsANoOpAndDoesNotDuplicateTheChartOfAccounts()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeProvisionedCompanyAsync(host);

        var auditRowsAfterFirst = (await host.InScopeAsync(AuditLogAccess.AllAsync)).Count;

        var second = await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .ProvisionAsync(companyId));

        second.AlreadyProvisioned.ShouldBeTrue();
        second.TotalRowsCreated.ShouldBe(0);

        (await ReadGroupsAsync(host, companyId)).Count.ShouldBe(28);
        (await ReadLedgersAsync(host, companyId)).Count.ShouldBe(9);

        var (types, series) = await ReadVoucherSetupAsync(host, companyId);
        types.Count.ShouldBe(8);
        series.Count.ShouldBe(8);

        // A no-op writes nothing, so it also writes no edit-log row: the log records changes, and
        // nothing changed.
        (await host.InScopeAsync(AuditLogAccess.AllAsync)).Count.ShouldBe(auditRowsAfterFirst);
    }

    [Fact]
    public async Task ProvisionAsync_ForASecondCompany_ScopesEveryRowToThatCompany()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var first = await ArrangeProvisionedCompanyAsync(host, "Vinayaka Agencies");
        var second = await ArrangeProvisionedCompanyAsync(host, "Vinayaka Pumps");

        // §10: the chart of accounts is company-scoped, so the same names co-exist under two
        // companies without colliding on any unique index.
        (await ReadGroupsAsync(host, first)).Count.ShouldBe(28);
        (await ReadGroupsAsync(host, second)).Count.ShouldBe(28);

        var firstCash = (await ReadLedgersAsync(host, first)).Single(l => l.Name == "Cash");
        var secondCash = (await ReadLedgersAsync(host, second)).Single(l => l.Name == "Cash");

        firstCash.Id.ShouldNotBe(secondCash.Id);
        firstCash.AccountGroupId.ShouldNotBe(secondCash.AccountGroupId);
    }

    [Fact]
    public async Task IsProvisionedAsync_BeforeAndAfterProvisioning_ReportsTheChartOfAccountsState()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var companyId = await AddCompanyAsync(host, "Vinayaka Agencies");

        (await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .IsProvisionedAsync(companyId))).ShouldBeFalse();

        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .ProvisionAsync(companyId));

        (await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .IsProvisionedAsync(companyId))).ShouldBeTrue();
    }

    [Fact]
    public async Task ProvisionAsync_ForACompanyThatDoesNotExist_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var refused = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<ICompanyProvisioningService>()
                .ProvisionAsync(Guid.NewGuid())));

        refused.Message.ShouldContain("does not exist");

        // Refused before anything was staged, so the books are untouched and the log is silent.
        (await host.InScopeAsync(AuditLogAccess.AllAsync)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_ForAnEmptyCompanyId_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<ICompanyProvisioningService>()
                .ProvisionAsync(Guid.Empty)));
    }

    /// <summary>Walks <c>ParentGroupId</c> to the root and rebuilds the path from ids alone.</summary>
    private static string RecomputePath(AccountGroup group, Dictionary<Guid, AccountGroup> byId)
    {
        var chain = new List<Guid>();
        var current = group;

        while (true)
        {
            chain.Insert(0, current.Id);

            if (current.ParentGroupId is not { } parentId)
            {
                break;
            }

            byId.TryGetValue(parentId, out var parent).ShouldBeTrue(
                $"'{current.Name}' references a parent that is not in the same company's tree");

            current = parent!;

            chain.Count.ShouldBeLessThan(
                AccountGroup.MaxDepth + 2, $"'{group.Name}' sits in a parent cycle");
        }

        return $"/{string.Join('/', chain.Select(id => id.ToString("N")))}/";
    }

    private static async Task<Guid> ArrangeProvisionedCompanyAsync(
        SqliteTestHost host,
        string name = "Vinayaka Agencies")
    {
        var companyId = await AddCompanyAsync(host, name);

        var result = await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .ProvisionAsync(companyId));

        result.AlreadyProvisioned.ShouldBeFalse();
        result.CompanyId.ShouldBe(companyId);
        result.AccountGroupsCreated.ShouldBe(28);
        result.LedgersCreated.ShouldBe(9);
        result.VoucherTypesCreated.ShouldBe(8);
        result.NumberSeriesCreated.ShouldBe(8);
        result.TotalRowsCreated.ShouldBe(53);

        return companyId;
    }

    private static Task<Guid> AddCompanyAsync(SqliteTestHost host, string name) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var company = new Company(name, $"{name} (Regd.)", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

    private static async Task<IReadOnlyList<AccountGroup>> ReadGroupsAsync(
        SqliteTestHost host,
        Guid companyId)
    {
        var groups = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<AccountGroup>()
            .ListAsync());

        return groups.Where(group => group.CompanyId == companyId).ToList();
    }

    private static async Task<IReadOnlyList<Ledger>> ReadLedgersAsync(
        SqliteTestHost host,
        Guid companyId)
    {
        var ledgers = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Ledger>()
            .ListAsync());

        return ledgers.Where(ledger => ledger.CompanyId == companyId).ToList();
    }

    private static async Task<(IReadOnlyList<VoucherType> Types, IReadOnlyList<VoucherNumberSeries> Series)>
        ReadVoucherSetupAsync(SqliteTestHost host, Guid companyId)
    {
        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var types = await unitOfWork.Repository<VoucherType>().ListAsync();
            var series = await unitOfWork.Repository<VoucherNumberSeries>().ListAsync();

            return ((IReadOnlyList<VoucherType>)types.Where(t => t.CompanyId == companyId).ToList(),
                (IReadOnlyList<VoucherNumberSeries>)series.Where(s => s.CompanyId == companyId).ToList());
        });
    }
}
