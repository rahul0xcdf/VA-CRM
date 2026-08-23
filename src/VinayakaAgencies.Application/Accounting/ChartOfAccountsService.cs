using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Maintains the chart of accounts: account groups and the ledgers that hang off them.
/// </summary>
/// <remarks>
/// All writes go through the unit of work so every change lands in the edit log, and all reads
/// resolve through <see cref="ICompanyContext"/> so nothing can cross company boundaries (spec §10).
/// Lifecycle follows spec §4.6: deactivate is always available; hard delete only when no journal
/// line references the row; system rows refuse both.
/// </remarks>
public sealed class ChartOfAccountsService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IChartOfAccountsService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <summary>The seeded bank groups whose subtree defines "bank ledger" for reconciliation.</summary>
    private static readonly string[] BankRootGroupNames = ["Bank Accounts", "Bank OD A/c"];

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountGroupSummary>> ListGroupsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var groups = await _unitOfWork.Repository<AccountGroup>()
            .ListAsync(new GroupsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        return groups
            .Where(group => includeInactive || group.IsActive)
            .OrderBy(group => group.MaterialisedPath, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerSummary>> ListLedgersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var groupsByNameId = await LoadGroupsByIdAsync(companyId, cancellationToken).ConfigureAwait(false);

        var ledgers = await _unitOfWork.Repository<Ledger>()
            .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        return ledgers
            .Where(ledger => includeInactive || ledger.IsActive)
            .OrderBy(ledger => ledger.NameNormalised, StringComparer.Ordinal)
            .Select(ledger => ToSummary(ledger, ResolveGroup(groupsByNameId, ledger)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerSummary>> ListBankLedgersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var groupsById = await LoadGroupsByIdAsync(companyId, cancellationToken).ConfigureAwait(false);

        // The same derivation the Contra rule uses, narrowed to banks: cash is not reconciled.
        var bankRootPaths = groupsById.Values
            .Where(group => BankRootGroupNames.Contains(group.NameNormalised, StringComparer.Ordinal))
            .Select(group => group.MaterialisedPath)
            .ToList();

        var ledgers = await _unitOfWork.Repository<Ledger>()
            .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        return ledgers
            .Where(ledger => includeInactive || ledger.IsActive)
            .Where(ledger => bankRootPaths.Any(path =>
                ResolveGroup(groupsById, ledger).MaterialisedPath.StartsWith(path, StringComparison.Ordinal)))
            .OrderBy(ledger => ledger.NameNormalised, StringComparer.Ordinal)
            .Select(ledger => ToSummary(ledger, ResolveGroup(groupsById, ledger)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<LedgerSummary?> GetLedgerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            return null;
        }

        var group = await _unitOfWork.Repository<AccountGroup>()
            .GetByIdAsync(ledger.AccountGroupId, cancellationToken)
            .ConfigureAwait(false);

        return ToSummary(
            ledger,
            group ?? throw new InvalidOperationException(
                $"Ledger '{ledger.Name}' points at group '{ledger.AccountGroupId}', which does not exist."));
    }

    /// <inheritdoc />
    public async Task<LedgerSummary> CreateLedgerAsync(
        CreateLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var name = RequireName(request.Name);
        var group = await RequireGroupAsync(companyId, request.AccountGroupId, cancellationToken)
            .ConfigureAwait(false);

        await EnsureLedgerNameFreeAsync(companyId, name, null, cancellationToken).ConfigureAwait(false);

        var ledger = new Ledger(
            companyId,
            name,
            request.AccountGroupId,
            alias: request.Alias,
            description: request.Description);

        await _unitOfWork.Repository<Ledger>().AddAsync(ledger, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(ledger, group);
    }

    /// <inheritdoc />
    public async Task<LedgerSummary> UpdateLedgerAsync(
        UpdateLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var repository = _unitOfWork.Repository<Ledger>();
        var ledger = await repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Ledger '{request.Id}' does not exist.");
        }

        var name = RequireName(request.Name);

        await EnsureLedgerNameFreeAsync(companyId, name, ledger.Id, cancellationToken).ConfigureAwait(false);

        if (request.AccountGroupId != ledger.AccountGroupId)
        {
            await RequireGroupAsync(companyId, request.AccountGroupId, cancellationToken).ConfigureAwait(false);
            ledger.MoveTo(request.AccountGroupId);
        }

        ledger.Rename(name, request.Alias);
        ledger.SetDescription(request.Description);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var group = await _unitOfWork.Repository<AccountGroup>()
            .GetByIdAsync(ledger.AccountGroupId, cancellationToken)
            .ConfigureAwait(false);

        return ToSummary(
            ledger,
            group ?? throw new InvalidOperationException(
                $"Ledger '{ledger.Name}' points at a group that does not exist."));
    }

    /// <inheritdoc />
    public async Task DeactivateLedgerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ledger = await RequireOwnLedgerAsync(id, cancellationToken).ConfigureAwait(false);

        ledger.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateLedgerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ledger = await RequireOwnLedgerAsync(id, cancellationToken).ConfigureAwait(false);

        ledger.Activate();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteLedgerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ledger = await RequireOwnLedgerAsync(id, cancellationToken).ConfigureAwait(false);

        if (ledger.IsSystem)
        {
            throw new InvalidOperationException(
                $"'{ledger.Name}' is a system ledger and cannot be deleted. Deactivate it instead.");
        }

        var postedTo = await _unitOfWork.Repository<JournalLine>()
            .AnyAsync(new JournalLinesOfLedger(id), cancellationToken)
            .ConfigureAwait(false);

        if (postedTo)
        {
            throw new InvalidOperationException(
                $"'{ledger.Name}' has journal lines against it and can no longer be deleted. " +
                "Deactivate it to keep its history intact.");
        }

        _unitOfWork.Repository<Ledger>().Remove(ledger);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AccountGroupSummary> CreateGroupAsync(
        CreateAccountGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var name = RequireName(request.Name);
        var parent = await RequireGroupAsync(companyId, request.ParentGroupId, cancellationToken)
            .ConfigureAwait(false);

        var siblings = await _unitOfWork.Repository<AccountGroup>()
            .ListAsync(new ChildGroupsOf(companyId, parent.Id), cancellationToken)
            .ConfigureAwait(false);

        if (siblings.Any(sibling =>
                string.Equals(sibling.NameNormalised, NameNormaliser.Normalise(name), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"A group named '{name}' already exists under '{parent.Name}'.");
        }

        var group = new AccountGroup(
            companyId,
            name,
            parent.Nature,
            parentGroupId: parent.Id,
            affectsGrossProfit: parent.AffectsGrossProfit,
            sortOrder: request.SortOrder);

        group.SetMaterialisedPath(parent.MaterialisedPath);

        await _unitOfWork.Repository<AccountGroup>().AddAsync(group, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(group);
    }

    /// <inheritdoc />
    public async Task<AccountGroupSummary> RenameGroupAsync(
        Guid id,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var repository = _unitOfWork.Repository<AccountGroup>();
        var group = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (group is null || group.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Account group '{id}' does not exist.");
        }

        // Sibling uniqueness is scoped by the nullable parent, so the comparison runs in memory:
        // an expression cannot branch on null versus Guid.Empty without tripping the very
        // NULL-collation trap §5 documents.
        var allGroups = await _unitOfWork.Repository<AccountGroup>()
            .ListAsync(new GroupsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var normalised = NameNormaliser.Normalise(newName);

        if (allGroups.Any(candidate =>
                candidate.Id != id
                && candidate.ParentGroupId == group.ParentGroupId
                && string.Equals(candidate.NameNormalised, normalised, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"A sibling group named '{newName.Trim()}' already exists.");
        }

        group.Rename(newName);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(group);
    }

    private async Task<Ledger> RequireOwnLedgerAsync(Guid id, CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Ledger '{id}' does not exist.");
        }

        return ledger;
    }

    private async Task<AccountGroup> RequireGroupAsync(
        Guid companyId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        if (groupId == Guid.Empty)
        {
            throw new InvalidOperationException("A group or ledger must be filed under an account group.");
        }

        var group = await _unitOfWork.Repository<AccountGroup>()
            .GetByIdAsync(groupId, cancellationToken)
            .ConfigureAwait(false);

        if (group is null || group.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Account group '{groupId}' does not exist.");
        }

        if (!group.IsActive)
        {
            throw new InvalidOperationException(
                $"Account group '{group.Name}' is inactive. Reactivate it or choose another group.");
        }

        return group;
    }

    private async Task EnsureLedgerNameFreeAsync(
        Guid companyId,
        string name,
        Guid? excludingLedgerId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(name);

        var existing = await _unitOfWork.Repository<Ledger>()
            .ListAsync(new LedgersNamed(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (existing.Any(ledger => ledger.Id != excludingLedgerId))
        {
            throw new InvalidOperationException($"A ledger named '{name}' already exists.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid, AccountGroup>> LoadGroupsByIdAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var groups = await _unitOfWork.Repository<AccountGroup>()
            .ListAsync(new GroupsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        return groups.ToDictionary(group => group.Id);
    }

    private static AccountGroup ResolveGroup(
        IReadOnlyDictionary<Guid, AccountGroup> groupsById,
        Ledger ledger) =>
        groupsById.TryGetValue(ledger.AccountGroupId, out var group)
            ? group
            : throw new InvalidOperationException(
                $"Ledger '{ledger.Name}' points at group '{ledger.AccountGroupId}', which does not exist.");

    private static string RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name.Trim();
    }

    private static AccountGroupSummary ToSummary(AccountGroup group) =>
        new(
            group.Id,
            group.Name,
            group.ParentGroupId,
            group.MaterialisedPath,
            group.Nature,
            group.AffectsGrossProfit,
            group.IsPrimary,
            group.IsSystem,
            group.IsActive,
            group.Depth,
            group.SortOrder);

    private static LedgerSummary ToSummary(Ledger ledger, AccountGroup group) =>
        new(
            ledger.Id,
            ledger.Name,
            ledger.Alias,
            group.Id,
            group.Name,
            group.Nature,
            ledger.IsSystem,
            ledger.AllowsManualPosting,
            ledger.IsActive,
            ledger.Description);

    private sealed class GroupsOfCompany : Specification<AccountGroup>
    {
        public GroupsOfCompany(Guid companyId)
        {
            Where(group => group.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class ChildGroupsOf : Specification<AccountGroup>
    {
        public ChildGroupsOf(Guid companyId, Guid parentId)
        {
            Where(group => group.CompanyId == companyId && group.ParentGroupId == parentId);
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

    private sealed class LedgersNamed : Specification<Ledger>
    {
        public LedgersNamed(Guid companyId, string nameNormalised)
        {
            Where(ledger => ledger.CompanyId == companyId && ledger.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class JournalLinesOfLedger : Specification<JournalLine>
    {
        public JournalLinesOfLedger(Guid ledgerId)
        {
            Where(line => line.LedgerId == ledgerId);
            ApplyNoTracking();
        }
    }
}
