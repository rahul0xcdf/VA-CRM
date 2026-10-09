using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Security;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Payroll;
using VinayakaAgencies.Domain.Printing;
using VinayakaAgencies.Domain.Tds;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Companies;

/// <summary>
/// Writes a new company's chart of accounts and voucher setup, through the unit of work.
/// </summary>
/// <remarks>
/// Everything this service creates goes in through <see cref="IRepository{TEntity}"/> and commits in
/// one <see cref="IUnitOfWork"/> transaction, for two reasons that are both non-negotiable:
/// <list type="number">
/// <item><description>The audit trail. Spec §11.1 requires every insert of every mapped entity to be
/// logged, and §5 rules out <c>HasData</c> precisely because migration seeding bypasses
/// <c>SaveChangesAsync</c>. A system-locked row appearing in the books with no edit-log row behind it
/// is the first thing an auditor would question.</description></item>
/// <item><description>Atomicity. A half-built chart of accounts — groups without their ledgers, or
/// voucher types without numbering series — is worse than none, because the next thing that happens
/// is someone posts into it.</description></item>
/// </list>
/// </remarks>
public sealed class CompanyProvisioningService(IUnitOfWork unitOfWork) : ICompanyProvisioningService
{
    private const int DefaultSeriesWidth = 4;
    private const long DefaultSeriesStartNumber = 1;

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    public async Task<bool> IsProvisionedAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        // Any one of the three is enough. Provisioning commits as a unit, so a company can only hold
        // all of them or none of them; asking about all three still means a partially hand-built
        // company is refused rather than half-seeded on top of.
        if (await _unitOfWork.Repository<AccountGroup>()
                .AnyAsync(new AccountGroupsOfCompanySpecification(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        if (await _unitOfWork.Repository<Ledger>()
                .AnyAsync(new LedgersOfCompanySpecification(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        return await _unitOfWork.Repository<VoucherType>()
            .AnyAsync(new VoucherTypesOfCompanySpecification(companyId), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CompanyProvisioningResult> ProvisionAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there are no books to provision.");
        }

        if (await IsProvisionedAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            return CompanyProvisioningResult.NoOp(companyId);
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var (groups, groupsByName) = BuildAccountGroups(companyId);
        await _unitOfWork.Repository<AccountGroup>()
            .AddRangeAsync(groups, cancellationToken)
            .ConfigureAwait(false);

        var ledgers = BuildLedgers(companyId, groupsByName);
        await _unitOfWork.Repository<Ledger>()
            .AddRangeAsync(ledgers, cancellationToken)
            .ConfigureAwait(false);

        var (voucherTypes, numberSeries) = BuildVoucherTypes(companyId);
        await _unitOfWork.Repository<VoucherType>()
            .AddRangeAsync(voucherTypes, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.Repository<VoucherNumberSeries>()
            .AddRangeAsync(numberSeries, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CompanyProvisioningResult(
            companyId,
            AlreadyProvisioned: false,
            groups.Count,
            ledgers.Count,
            voucherTypes.Count,
            numberSeries.Count);
    }

    /// <summary>
    /// The §4.3 primary groups and the §4.4 sub-groups, each carrying the materialised path its
    /// parent chain implies (§4.2).
    /// </summary>
    /// <remarks>
    /// The path is set here, at construction, and never left for a later pass: it is a cache of
    /// <c>ParentGroupId</c>, and a row that reaches the database with the two disagreeing would make
    /// every group roll-up quietly wrong rather than loudly broken.
    /// </remarks>
    private static (List<AccountGroup> Groups, Dictionary<string, AccountGroup> ByName)
        BuildAccountGroups(Guid companyId)
    {
        var groups = new List<AccountGroup>(
            CompanySeedData.PrimaryGroups.Count + CompanySeedData.SubGroups.Count);

        var byName = new Dictionary<string, AccountGroup>(StringComparer.Ordinal);
        var sortOrder = 0;

        foreach (var seed in CompanySeedData.PrimaryGroups)
        {
            var group = new AccountGroup(
                companyId,
                seed.Name,
                seed.Nature,
                parentGroupId: null,
                affectsGrossProfit: seed.AffectsGrossProfit,
                isPrimary: true,
                isSystem: true,
                sortOrder: ++sortOrder);

            group.SetMaterialisedPath(null);

            groups.Add(group);
            byName.Add(seed.Name, group);
        }

        foreach (var seed in CompanySeedData.SubGroups)
        {
            var parent = Resolve(byName, seed.ParentName, seed.Name);

            // Nature and AffectsGrossProfit are inherited, not chosen: a sub-group that disagreed
            // with its parent would land its balances on the wrong statement.
            var group = new AccountGroup(
                companyId,
                seed.Name,
                parent.Nature,
                parentGroupId: parent.Id,
                affectsGrossProfit: parent.AffectsGrossProfit,
                isPrimary: false,
                isSystem: true,
                sortOrder: ++sortOrder);

            group.SetMaterialisedPath(parent.MaterialisedPath);

            groups.Add(group);
            byName.Add(seed.Name, group);
        }

        return (groups, byName);
    }

    private static List<Ledger> BuildLedgers(
        Guid companyId,
        IReadOnlyDictionary<string, AccountGroup> groupsByName)
    {
        var ledgers = new List<Ledger>(CompanySeedData.Ledgers.Count);

        foreach (var seed in CompanySeedData.Ledgers)
        {
            var group = Resolve(groupsByName, seed.GroupName, seed.Name);

            ledgers.Add(new Ledger(
                companyId,
                seed.Name,
                group.Id,
                alias: null,
                isSystem: seed.IsSystem,
                allowsManualPosting: seed.AllowsManualPosting));
        }

        return ledgers;
    }

    /// <summary>
    /// The eight §7.1 system voucher types, each with its §7.2 default numbering series.
    /// </summary>
    /// <remarks>
    /// <c>DefaultSeriesId</c> is set <i>before</i> the type is staged, not in a second save. It
    /// carries no foreign key by design (§5) precisely so the mutually referencing pair can be
    /// inserted in one pass; writing it afterwards would produce an <c>UPDATE</c>, and therefore a
    /// spurious edit-log row, on every company provisioning. Client-generated ids (ADR-0001 §5) are
    /// what make the forward reference possible.
    /// </remarks>
    private static (List<VoucherType> Types, List<VoucherNumberSeries> Series)
        BuildVoucherTypes(Guid companyId)
    {
        var types = new List<VoucherType>(CompanySeedData.VoucherTypes.Count);
        var series = new List<VoucherNumberSeries>(CompanySeedData.VoucherTypes.Count);

        foreach (var seed in CompanySeedData.VoucherTypes)
        {
            var voucherType = new VoucherType(companyId, seed.Name, seed.Kind, isSystem: true);

            var defaultSeries = new VoucherNumberSeries(
                companyId,
                voucherType.Id,
                seed.Name,
                seed.Prefix,
                width: DefaultSeriesWidth,
                suffix: null,
                startNumber: DefaultSeriesStartNumber,
                resetPolicy: NumberResetPolicy.FinancialYear,
                allowManualOverride: true);

            voucherType.SetDefaultSeries(defaultSeries.Id);

            types.Add(voucherType);
            series.Add(defaultSeries);
        }

        return (types, series);
    }

    private static AccountGroup Resolve(
        IReadOnlyDictionary<string, AccountGroup> groupsByName,
        string groupName,
        string dependentName)
    {
        if (groupsByName.TryGetValue(groupName, out var group))
        {
            return group;
        }

        throw new InvalidOperationException(
            $"Seed row '{dependentName}' names the group '{groupName}', which is not among the " +
            "seeded groups. The seed tables and the spec's §4.3/§4.4 tables have diverged.");
    }

    /// <summary>
    /// The inventory seed: one system voucher type ("Stock Journal", Kind=Journal, prefix SJ/), the
    /// system "Main Location" godown, the base "NOS" unit and the settings row defaulting to FIFO
    /// (phase-2-inventory.md §1; open-questions.md #19).
    /// </summary>
    /// <remarks>
    /// Row-granular idempotence on purpose: a company provisioned under Phase 1 has no stock rows,
    /// while one provisioned after Phase 2 has them all. Each piece is checked independently so the
    /// call can run at every startup and only ever fill what is genuinely missing.
    /// </remarks>
    public async Task EnsureInventorySeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there is nothing to seed inventory into.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await EnsureStockJournalVoucherTypeAsync(companyId, cancellationToken).ConfigureAwait(false);
        await EnsureMainGodownAsync(companyId, cancellationToken).ConfigureAwait(false);
        await EnsureBaseUnitAsync(companyId, cancellationToken).ConfigureAwait(false);
        await EnsureInventorySettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureStockJournalVoucherTypeAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(InventorySeedData.StockJournalVoucherTypeName);

        if (await _unitOfWork.Repository<VoucherType>()
                .AnyAsync(new VoucherTypeByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var voucherType = new VoucherType(
            companyId,
            InventorySeedData.StockJournalVoucherTypeName,
            VoucherKind.Journal,
            isSystem: true);

        var series = new VoucherNumberSeries(
            companyId,
            voucherType.Id,
            InventorySeedData.StockJournalVoucherTypeName,
            InventorySeedData.StockJournalPrefix,
            width: DefaultSeriesWidth,
            suffix: null,
            startNumber: DefaultSeriesStartNumber,
            resetPolicy: NumberResetPolicy.FinancialYear,
            allowManualOverride: true);

        voucherType.SetDefaultSeries(series.Id);

        await _unitOfWork.Repository<VoucherType>()
            .AddAsync(voucherType, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.Repository<VoucherNumberSeries>()
            .AddAsync(series, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureMainGodownAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(Godown.MainLocationName);

        if (await _unitOfWork.Repository<Domain.Inventory.Godown>()
                .AnyAsync(new GodownByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await _unitOfWork.Repository<Domain.Inventory.Godown>().AddAsync(
            new Domain.Inventory.Godown(companyId, Godown.MainLocationName, address: null, isSystem: true),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBaseUnitAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(InventorySeedData.BaseUnitCode);

        if (await _unitOfWork.Repository<Domain.Inventory.UnitOfMeasure>()
                .AnyAsync(new UnitByCode(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await _unitOfWork.Repository<Domain.Inventory.UnitOfMeasure>().AddAsync(
            new Domain.Inventory.UnitOfMeasure(
                companyId,
                InventorySeedData.BaseUnitCode,
                decimalPlaces: 0,
                description: "Numbers (default base unit)"),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInventorySettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (await _unitOfWork.Repository<Domain.Inventory.InventorySettings>()
                .AnyAsync(new SettingsByCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await _unitOfWork.Repository<Domain.Inventory.InventorySettings>().AddAsync(
            new Domain.Inventory.InventorySettings(companyId, ValuationMethod.Fifo),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The pipeline seed: one system voucher type with its numbering series per trade-document
    /// kind, the cost-of-goods-sold ledger, and the default tax-invoice print templates
    /// (phase-3-sales-purchase-pipeline.md §4a).
    /// </summary>
    /// <remarks>
    /// Row-granular and idempotent for the same reason as <see cref="EnsureInventorySeedAsync"/>:
    /// companies provisioned under earlier phases must gain exactly what is missing at the next
    /// startup, and nothing once nothing is missing.
    /// </remarks>
    public async Task EnsurePipelineSeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there is nothing to seed the pipeline into.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var kind in PipelineSeedData.Kinds)
        {
            await EnsureKindVoucherTypeAsync(companyId, kind, cancellationToken).ConfigureAwait(false);
        }

        await EnsureCostOfGoodsSoldLedgerAsync(companyId, cancellationToken).ConfigureAwait(false);
        await EnsureDefaultTemplatesAsync(companyId, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The customer seed: the four placeholder categories and the sales settings row
    /// (phase-4-customer-pricing.md §2, §4a). Row-granular and idempotent for the same reason as
    /// <see cref="EnsureInventorySeedAsync"/>.
    /// </summary>
    public async Task EnsureCustomerSeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there is nothing to seed customers into.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var seed in CustomerSeedData.Categories)
        {
            var normalised = NameNormaliser.Normalise(seed.Name);

            if (await _unitOfWork.Repository<CustomerCategory>()
                    .AnyAsync(new CustomerCategoryByName(companyId, normalised), cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            await _unitOfWork.Repository<CustomerCategory>().AddAsync(
                new CustomerCategory(companyId, seed.Name, seed.Description),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!await _unitOfWork.Repository<SalesSettings>()
                .AnyAsync(new SalesSettingsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            await _unitOfWork.Repository<SalesSettings>().AddAsync(
                new SalesSettings(companyId),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The GST seed: the output/input tax ledgers and the settings row naming the round-off ledger
    /// (phase-5-gst-returns.md §2). Row-granular and idempotent for the same reason as its
    /// Phase 2/3/4 siblings. No HSN codes and no rates are seeded — those are master data, not
    /// provisioning (CLAUDE.md non-negotiable #6).
    /// </summary>
    public async Task EnsureGstSeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there is nothing to seed GST into.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var ledgerName in GstSeedData.OutputLedgerNames)
        {
            await EnsureTaxLedgerAsync(
                    companyId,
                    ledgerName,
                    GstSeedData.DutiesAndTaxesGroupName,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var ledgerName in GstSeedData.InputLedgerNames)
        {
            await EnsureTaxLedgerAsync(
                    companyId,
                    ledgerName,
                    GstSeedData.CurrentAssetsGroupName,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!await _unitOfWork.Repository<GstSettings>()
                .AnyAsync(new GstSettingsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            await _unitOfWork.Repository<GstSettings>().AddAsync(
                new GstSettings(companyId, GstSettings.DefaultRoundOffLedgerName),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The TDS/TCS seed: the two liability ledgers and the settings row naming them
    /// (phase-7-tds-payroll-banking.md §2). Row-granular and idempotent like its siblings.
    /// </summary>
    /// <remarks>
    /// <b>No section, rate or threshold is seeded</b> — not even 194C, which §4a D1 names as the
    /// only section in use today. Those are the owner's master data (non-negotiable #6), and a
    /// seeded rate is a rate nobody owns when the notification changes it. See
    /// <see cref="TdsSeedData"/>.
    /// <para>
    /// Both applicability flags start off, which is §4a D2's whole point: a business below the
    /// turnover limit carries two booleans and no behaviour, and crossing the limit later is a
    /// settings change rather than a migration.
    /// </para>
    /// </remarks>
    public async Task EnsureTdsSeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there is nothing to seed TDS into.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var ledgerName in TdsSeedData.PayableLedgerNames)
        {
            await EnsureTaxLedgerAsync(
                    companyId,
                    ledgerName,
                    TdsSeedData.DutiesAndTaxesGroupName,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!await _unitOfWork.Repository<TdsSettings>()
                .AnyAsync(new TdsSettingsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            await _unitOfWork.Repository<TdsSettings>().AddAsync(
                new TdsSettings(
                    companyId,
                    TdsSeedData.TdsPayableLedgerName,
                    TdsSeedData.TcsPayableLedgerName),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // The cheque layout, as a Phase 3 print template of kind Cheque — not a second template
        // system (§4a D4). One adjustable CTS-2010 layout, no bank named and none seeded: the
        // owner measures against a real cheque and saves a new version, which is checkpoint C7.8.
        await EnsureChequeTemplateAsync(companyId, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The payroll seed: the salary and statutory ledgers, and the settings row
    /// (phase-7-tds-payroll-banking.md §4a D3). Row-granular and idempotent like its siblings.
    /// </summary>
    /// <remarks>
    /// <b>No rate, ceiling, slab or pay head is seeded.</b> §2 extends non-negotiable #6 to PF and
    /// ESI contribution rates, their wage ceilings and Professional Tax slabs by name; pay heads
    /// are the owner's own salary structure. See <see cref="PayrollSeedData"/>.
    /// <para>
    /// All three statutory flags start off, which is §4a D3's answer to a headcount nobody had at
    /// approval: with them off the module is inert (checkpoint C7.7), and switching PF on when the
    /// business crosses twenty employees is a settings change rather than a migration.
    /// </para>
    /// </remarks>
    public async Task EnsurePayrollSeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there is nothing to seed payroll into.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var (ledgerName, groupName) in PayrollSeedData.Ledgers)
        {
            await EnsureTaxLedgerAsync(companyId, ledgerName, groupName, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!await _unitOfWork.Repository<PayrollSettings>()
                .AnyAsync(new PayrollSettingsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
        {
            await _unitOfWork.Repository<PayrollSettings>().AddAsync(
                new PayrollSettings(companyId, PayrollSeedData.SalaryPayableLedgerName),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureChequeTemplateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(ChequeLayoutDefaults.DefaultTemplateName);

        if (await _unitOfWork.Repository<PrintTemplate>()
                .AnyAsync(
                    new TemplateByName(companyId, PrintDocumentKind.Cheque, normalised),
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var template = new PrintTemplate(
            companyId,
            PrintDocumentKind.Cheque,
            ChequeLayoutDefaults.DefaultTemplateName,
            priority: 10,
            description: "Field positions for a standard CTS-2010 leaf. Adjust against a real cheque.");

        var revision = new PrintTemplateRevision(
            companyId,
            template.Id,
            version: 1,
            PrintTemplateService.Serialise(ChequeLayoutDefaults.Default));

        template.AttachRevision(revision.Id, revision.Version);

        await _unitOfWork.Repository<PrintTemplate>().AddAsync(template, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.Repository<PrintTemplateRevision>().AddAsync(revision, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureTaxLedgerAsync(
        Guid companyId,
        string ledgerName,
        string groupName,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(ledgerName);

        if (await _unitOfWork.Repository<Ledger>()
                .AnyAsync(new LedgerByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var group = await _unitOfWork.Repository<AccountGroup>()
            .SingleOrDefaultAsync(
                new GroupByName(companyId, NameNormaliser.Normalise(groupName)),
                cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
        {
            throw new InvalidOperationException(
                $"The '{groupName}' account group is missing from this company's chart of " +
                $"accounts, so the '{ledgerName}' ledger has nowhere to hang. Provision the " +
                "company first.");
        }

        await _unitOfWork.Repository<Ledger>().AddAsync(
            new Ledger(companyId, ledgerName, group.Id),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureKindVoucherTypeAsync(
        Guid companyId,
        PipelineSeedData.KindSeed kind,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(kind.VoucherTypeName);

        if (await _unitOfWork.Repository<VoucherType>()
                .AnyAsync(new VoucherTypeByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var voucherType = new VoucherType(companyId, kind.VoucherTypeName, kind.AccountingKind, isSystem: true);

        var series = new VoucherNumberSeries(
            companyId,
            voucherType.Id,
            kind.VoucherTypeName,
            kind.Prefix,
            width: DefaultSeriesWidth,
            suffix: null,
            startNumber: DefaultSeriesStartNumber,
            resetPolicy: NumberResetPolicy.FinancialYear,
            allowManualOverride: true);

        voucherType.SetDefaultSeries(series.Id);

        await _unitOfWork.Repository<VoucherType>()
            .AddAsync(voucherType, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.Repository<VoucherNumberSeries>()
            .AddAsync(series, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCostOfGoodsSoldLedgerAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(PipelineSeedData.CostOfGoodsSoldLedgerName);

        if (await _unitOfWork.Repository<Ledger>()
                .AnyAsync(new LedgerByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var group = await _unitOfWork.Repository<AccountGroup>()
            .SingleOrDefaultAsync(
                new GroupByName(companyId, NameNormaliser.Normalise(PipelineSeedData.CostOfGoodsSoldGroupName)),
                cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
        {
            throw new InvalidOperationException(
                $"The '{PipelineSeedData.CostOfGoodsSoldGroupName}' account group is missing from "
                + "this company's chart of accounts, so the cost-of-goods-sold ledger has nowhere "
                + "to hang. Provision the company first.");
        }

        await _unitOfWork.Repository<Ledger>().AddAsync(
            new Ledger(
                companyId,
                PipelineSeedData.CostOfGoodsSoldLedgerName,
                group.Id,
                alias: null,
                isSystem: false,
                allowsManualPosting: true),
            cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDefaultTemplatesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        foreach (var seed in PrintTemplateDefaults.Seeds)
        {
            var normalised = NameNormaliser.Normalise(seed.Name);

            if (await _unitOfWork.Repository<PrintTemplate>()
                    .AnyAsync(new TemplateByName(companyId, seed.DocumentKind, normalised), cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            var template = new PrintTemplate(
                companyId,
                seed.DocumentKind,
                seed.Name,
                seed.Priority,
                seed.Description);

            // Version 1 lands with the template itself; the designer appends from version 2 on.
            var revision = new PrintTemplateRevision(
                companyId,
                template.Id,
                version: 1,
                PrintTemplateService.Serialise(seed.Layout));

            template.AttachRevision(revision.Id, revision.Version);

            await _unitOfWork.Repository<PrintTemplate>()
                .AddAsync(template, cancellationToken)
                .ConfigureAwait(false);
            await _unitOfWork.Repository<PrintTemplateRevision>()
                .AddAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task EnsureSecuritySeedAsync(
        Guid companyId,
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
                $"Company '{companyId}' does not exist, so there are no roles to seed into it.");
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // The same helper first-account creation uses, so the Operator role cannot start with one
        // set of grants on a fresh install and a different set on an upgraded one.
        await SecuritySeed
            .EnsureRolesAsync(_unitOfWork, companyId, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class LedgerByName : Specification<Ledger>
    {
        public LedgerByName(Guid companyId, string nameNormalised)
        {
            Where(ledger => ledger.CompanyId == companyId && ledger.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class CustomerCategoryByName : Specification<CustomerCategory>
    {
        public CustomerCategoryByName(Guid companyId, string nameNormalised)
        {
            Where(category => category.CompanyId == companyId
                && category.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class SalesSettingsOfCompany : Specification<SalesSettings>
    {
        public SalesSettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
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

    private sealed class TemplateByName : Specification<PrintTemplate>
    {
        public TemplateByName(Guid companyId, PrintDocumentKind documentKind, string nameNormalised)
        {
            Where(template => template.CompanyId == companyId
                && template.DocumentKind == documentKind
                && template.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class AccountGroupsOfCompanySpecification : Specification<AccountGroup>
    {
        public AccountGroupsOfCompanySpecification(Guid companyId)
        {
            Where(group => group.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class LedgersOfCompanySpecification : Specification<Ledger>
    {
        public LedgersOfCompanySpecification(Guid companyId)
        {
            Where(ledger => ledger.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class VoucherTypesOfCompanySpecification : Specification<VoucherType>
    {
        public VoucherTypesOfCompanySpecification(Guid companyId)
        {
            Where(voucherType => voucherType.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class VoucherTypeByName : Specification<VoucherType>
    {
        public VoucherTypeByName(Guid companyId, string nameNormalised)
        {
            Where(voucherType => voucherType.CompanyId == companyId
                && voucherType.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class GodownByName : Specification<Godown>
    {
        public GodownByName(Guid companyId, string nameNormalised)
        {
            Where(godown => godown.CompanyId == companyId && godown.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class UnitByCode : Specification<UnitOfMeasure>
    {
        public UnitByCode(Guid companyId, string codeNormalised)
        {
            Where(unit => unit.CompanyId == companyId && unit.CodeNormalised == codeNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class SettingsByCompany : Specification<InventorySettings>
    {
        public SettingsByCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class GstSettingsOfCompany : Specification<GstSettings>
    {
        public GstSettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class PayrollSettingsOfCompany : Specification<PayrollSettings>
    {
        public PayrollSettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class TdsSettingsOfCompany : Specification<TdsSettings>
    {
        public TdsSettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
