using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

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
}
