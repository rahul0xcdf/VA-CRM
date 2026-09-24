using System.Linq.Expressions;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// What the company already holds, so an import can tell a new record from one it has seen before.
/// </summary>
/// <remarks>
/// <para>
/// Loaded once per pass and kept in memory. Masters number in the thousands even after a full
/// history migration, where vouchers number in the hundreds of thousands — so the masters are the
/// part it is safe to hold, and holding them is what keeps the import from running a lookup query
/// per record.
/// </para>
/// <para>
/// Both passes use this, and the validation pass registers records it has <i>not</i> written yet.
/// That is what lets a file create a unit on line 10 and a stock item naming it on line 40 without
/// the validator reporting a missing unit for a file that is perfectly correct.
/// </para>
/// </remarks>
internal sealed class InteropMasterIndex
{
    private InteropMasterIndex(Guid companyId)
    {
        CompanyId = companyId;
    }

    public Guid CompanyId { get; }

    public InteropNameMap Units { get; } = new("unit");

    public InteropNameMap StockGroups { get; } = new("stock group");

    public InteropNameMap Godowns { get; } = new("godown");

    public InteropNameMap Items { get; } = new("stock item");

    public InteropNameMap Ledgers { get; } = new("ledger");

    public InteropNameMap HsnCodes { get; } = new("HSN/SAC code");

    public InteropNameMap CostCentres { get; } = new("cost centre");

    /// <summary>
    /// Account groups are held whole, not just as ids.
    /// </summary>
    /// <remarks>
    /// A new group has to be given its materialised path at construction, and the path is built from
    /// its parent's. <c>CompanyProvisioningService</c> makes the same point: a row that reaches the
    /// database with path and parent disagreeing makes every group roll-up quietly wrong rather than
    /// loudly broken.
    /// </remarks>
    public Dictionary<string, AccountGroup> AccountGroupsByName { get; } = new(StringComparer.Ordinal);

    public Dictionary<Guid, AccountGroup> AccountGroupsById { get; } = [];

    public Dictionary<string, VoucherType> VoucherTypesByName { get; } = new(StringComparer.Ordinal);

    public Dictionary<VoucherKind, VoucherType> VoucherTypesByKind { get; } = [];

    /// <summary>Items by id, for the tracking flags a stock line needs at construction.</summary>
    public Dictionary<Guid, Item> ItemsById { get; } = [];

    /// <summary>
    /// Vouchers already in the books, keyed the way the numbering index makes them unique.
    /// </summary>
    /// <remarks>
    /// A voucher number repeats across financial years by design — the series resets each April
    /// (open-questions.md #11) — so the year has to be part of the key or a re-import of this year's
    /// SAL/0001 would match last year's and alter the wrong voucher.
    /// </remarks>
    public Dictionary<string, Guid> VoucherIdByTypeNumberAndYear { get; } = new(StringComparer.Ordinal);

    public HashSet<Guid> VoucherIds { get; } = [];

    /// <summary>The month the financial year starts in, for keying voucher numbers by year.</summary>
    public int FinancialYearStartMonth { get; private set; } = 4;

    public static async Task<InteropMasterIndex> LoadAsync(
        IUnitOfWork unitOfWork,
        Guid companyId,
        int financialYearStartMonth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        var index = new InteropMasterIndex(companyId) { FinancialYearStartMonth = financialYearStartMonth };

        foreach (var unit in await ListAsync<UnitOfMeasure>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.Units.Register(unit.Id, unit.Code);
        }

        foreach (var group in await ListAsync<StockGroup>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.StockGroups.Register(group.Id, group.Name);
        }

        foreach (var godown in await ListAsync<Godown>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.Godowns.Register(godown.Id, godown.Name);
        }

        foreach (var item in await ListAsync<Item>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.Items.Register(item.Id, item.Name);
            index.ItemsById[item.Id] = item;
        }

        foreach (var group in await ListAsync<AccountGroup>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.RegisterAccountGroup(group);
        }

        foreach (var ledger in await ListAsync<Ledger>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.Ledgers.Register(ledger.Id, ledger.Name);
        }

        foreach (var code in await ListAsync<HsnSac>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.HsnCodes.Register(code.Id, code.Code);
        }

        foreach (var centre in await ListAsync<CostCentre>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.CostCentres.Register(centre.Id, centre.Name);
        }

        foreach (var type in await ListAsync<VoucherType>(unitOfWork, x => x.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            index.VoucherTypesByName[NameNormaliser.Normalise(type.Name)] = type;

            // Several types can share one kind — the eight seeded ones, plus "Stock Journal" and the
            // Phase 3 invoice types, plus whatever the owner adds. The fallback for a voucher whose
            // own type name is not recognised has to be deterministic, so the type actually named
            // after its kind wins outright and anything else only fills an empty slot. Letting
            // enumeration order decide would send imported journals to "Stock Journal" on one install
            // and to "Journal" on the next.
            var isCanonical = NameNormaliser.Normalise(type.Name)
                == NameNormaliser.Normalise(TallyConventions.VoucherTypeName(type.Kind));

            if (isCanonical || !index.VoucherTypesByKind.ContainsKey(type.Kind))
            {
                index.VoucherTypesByKind[type.Kind] = type;
            }
        }

        foreach (var revision in await ListAsync<VoucherRevision>(
            unitOfWork,
            x => x.CompanyId == companyId && x.SupersededByRevisionId == null,
            cancellationToken).ConfigureAwait(false))
        {
            index.VoucherIds.Add(revision.VoucherId);
            index.VoucherIdByTypeNumberAndYear[index.VoucherKey(
                revision.VoucherTypeId,
                revision.Number,
                revision.VoucherDate)] = revision.VoucherId;
        }

        return index;
    }

    /// <summary>
    /// The voucher type a voucher should post under: its own name first, its behaviour second.
    /// </summary>
    /// <remarks>
    /// The name is preferred because it is what the source actually filed the voucher as, and a
    /// company's numbering series hangs off the type. Falling back to the kind is what lets a Tally
    /// file whose voucher types were not exported still land its vouchers somewhere sensible rather
    /// than being refused wholesale.
    /// </remarks>
    public VoucherType? ResolveVoucherType(string name, VoucherKind? kind)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && VoucherTypesByName.TryGetValue(NameNormaliser.Normalise(name), out var byName))
        {
            return byName;
        }

        return kind is { } value && VoucherTypesByKind.TryGetValue(value, out var byKind) ? byKind : null;
    }

    /// <summary>
    /// Records a voucher type the file defines but the books have not got yet.
    /// </summary>
    /// <remarks>
    /// The validation pass needs it so the vouchers filed under it resolve; the writer creates the
    /// real row. The entity built here is never persisted — it exists to answer "will this name
    /// exist by the time that voucher is read", which is the only question validation asks.
    /// </remarks>
    public void RegisterProvisionalVoucherType(string name, VoucherKind kind)
    {
        var provisional = new VoucherType(CompanyId, name, kind);

        VoucherTypesByName[NameNormaliser.Normalise(name)] = provisional;
        VoucherTypesByKind.TryAdd(kind, provisional);
    }

    public void RegisterVoucherType(VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        VoucherTypesByName[NameNormaliser.Normalise(type.Name)] = type;
        VoucherTypesByKind.TryAdd(type.Kind, type);
    }

    public void RegisterAccountGroup(AccountGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        AccountGroupsByName[NameNormaliser.Normalise(group.Name)] = group;
        AccountGroupsById[group.Id] = group;
    }

    public AccountGroup? FindAccountGroup(Guid? id, string name)
    {
        if (id is { } value && AccountGroupsById.TryGetValue(value, out var byId))
        {
            return byId;
        }

        return AccountGroupsByName.TryGetValue(NameNormaliser.Normalise(name), out var byName) ? byName : null;
    }

    public AccountGroup? ResolveAccountGroup(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : AccountGroupsByName.TryGetValue(NameNormaliser.Normalise(name), out var group) ? group : null;

    /// <summary>The key a voucher is unique under: its type, its number and its financial year.</summary>
    public string VoucherKey(Guid voucherTypeId, string number, DateOnly date)
    {
        var year = date.Month >= FinancialYearStartMonth ? date.Year : date.Year - 1;
        return $"{voucherTypeId:N}|{VoucherRevision.Normalise(number ?? string.Empty)}|{year}";
    }

    private static Task<IReadOnlyList<TEntity>> ListAsync<TEntity>(
        IUnitOfWork unitOfWork,
        Expression<Func<TEntity, bool>> criteria,
        CancellationToken cancellationToken)
        where TEntity : Entity =>
        unitOfWork.Repository<TEntity>().ListAsync(new CriteriaSpecification<TEntity>(criteria), cancellationToken);

    private sealed class CriteriaSpecification<TEntity> : Specification<TEntity>
        where TEntity : Entity
    {
        public CriteriaSpecification(Expression<Func<TEntity, bool>> criteria) => Where(criteria);
    }
}

/// <summary>
/// One master kind's names, and the identity each resolves to.
/// </summary>
/// <remarks>
/// Matching goes by GUID first and by normalised name second, and both halves matter for a different
/// reason. The GUID is what makes a re-import of this application's own export update rather than
/// duplicate (C8.2). The name is what makes an import from a Tally company that has never seen our
/// GUIDs land on the right records anyway — and it is also the only identity available for records
/// created here, because an entity generates its own id at construction (ADR-0001) and no importer
/// can dictate one from outside.
/// </remarks>
internal sealed class InteropNameMap
{
    private readonly Dictionary<string, Guid> _byName = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _ids = [];

    public InteropNameMap(string kind)
    {
        Kind = kind;
    }

    /// <summary>What this holds, for an error message that names it the way the owner would.</summary>
    public string Kind { get; }

    public int Count => _byName.Count;

    public void Register(Guid id, string name)
    {
        _byName[NameNormaliser.Normalise(name)] = id;
        _ids.Add(id);
    }

    public bool Knows(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.ContainsKey(NameNormaliser.Normalise(name));

    public Guid? Resolve(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(NameNormaliser.Normalise(name), out var id)
            ? id
            : null;

    /// <summary>The existing record this one refers to, or null when it is new.</summary>
    public Guid? Match(Guid? id, string name)
    {
        if (id is { } value && _ids.Contains(value))
        {
            return value;
        }

        return Resolve(name);
    }

    /// <summary>
    /// True when the record's GUID and its name point at two different existing records.
    /// </summary>
    /// <remarks>
    /// Reported rather than resolved. Renaming the GUID-matched record onto the other one's name
    /// would collide with the unique index, and picking either silently would change a master the
    /// owner never asked about.
    /// </remarks>
    public bool IsAmbiguous(Guid? id, string name)
    {
        if (id is not { } value || !_ids.Contains(value))
        {
            return false;
        }

        var byName = Resolve(name);

        return byName is not null && byName != value;
    }
}
