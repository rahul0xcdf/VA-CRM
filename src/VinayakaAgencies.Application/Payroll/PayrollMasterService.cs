using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Application.Payroll;

/// <summary>
/// The payroll masters (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// Dated rows everywhere, appended and closed rather than edited, for the same reason
/// <c>TaxSectionService</c> does it: a pay slip issued in September has to stay reproducible after
/// an October raise, and a PF remittance made at the old rate has to stay reconstructible after the
/// rate moves. Every append here closes the open-ended row it supersedes the day before it begins,
/// so exactly one row covers any date.
/// </para>
/// <para>
/// No rate, ceiling or slab is seeded anywhere. §2 extends non-negotiable #6 to PF and ESI
/// contribution rates, their wage ceilings and PT slabs by name; the owner enters what their state
/// and their notification say, and nothing in <c>src/</c> holds a figure of its own.
/// </para>
/// </remarks>
public sealed class PayrollMasterService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IPayrollMasterService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmployeeSummary>> ListEmployeesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<Employee>()
                .ListAsync(new EmployeesOfCompany(companyId, includeInactive), cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(employee => employee.Code, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<EmployeeSummary> CreateEmployeeAsync(
        SaveEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _unitOfWork.Repository<Employee>()
                .AnyAsync(new ActiveEmployeeByCode(companyId, code), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Employee code '{code}' is already in use.");
        }

        var employee = new Employee(
            companyId,
            request.Code,
            request.Name,
            request.JoinedOn,
            request.Designation,
            request.Pan,
            request.Uan,
            request.EsiNumber);

        if (request.LeftOn is { } leftOn)
        {
            employee.RecordLeaving(leftOn);
        }

        await _unitOfWork.Repository<Employee>().AddAsync(employee, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(employee);
    }

    /// <inheritdoc />
    public async Task<EmployeeSummary> UpdateEmployeeAsync(
        Guid employeeId,
        SaveEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var employee = await RequireEmployeeAsync(companyId, employeeId, cancellationToken)
            .ConfigureAwait(false);

        employee.SetName(request.Name);
        employee.SetDesignation(request.Designation);
        employee.SetPan(request.Pan);
        employee.SetUan(request.Uan);
        employee.SetEsiNumber(request.EsiNumber);

        if (request.LeftOn is { } leftOn)
        {
            employee.RecordLeaving(leftOn);
        }
        else
        {
            employee.ClearLeaving();
        }

        _unitOfWork.Repository<Employee>().Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(employee);
    }

    /// <inheritdoc />
    public async Task<EmployeeSummary> SetEmployeeActiveAsync(
        Guid employeeId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var employee = await RequireEmployeeAsync(companyId, employeeId, cancellationToken)
            .ConfigureAwait(false);

        if (isActive)
        {
            employee.Activate();
        }
        else
        {
            employee.Deactivate();
        }

        _unitOfWork.Repository<Employee>().Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(employee);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PayHeadSummary>> ListPayHeadsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var heads = await _unitOfWork.Repository<PayHead>()
            .ListAsync(new PayHeadsOfCompany(companyId, includeInactive), cancellationToken)
            .ConfigureAwait(false);

        var ledgers = await LedgerNamesAsync(companyId, cancellationToken).ConfigureAwait(false);

        return heads
            .OrderBy(head => head.Type)
            .ThenBy(head => head.SortOrder)
            .ThenBy(head => head.Name, StringComparer.OrdinalIgnoreCase)
            .Select(head => ToSummary(head, ledgers))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PayHeadSummary> CreatePayHeadAsync(
        SavePayHeadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var normalised = NameNormaliser.Normalise(request.Name);

        if (await _unitOfWork.Repository<PayHead>()
                .AnyAsync(new ActivePayHeadByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException($"A pay head named '{request.Name}' already exists.");
        }

        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(request.LedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            throw new InvalidOperationException("That ledger does not exist in this company's books.");
        }

        // Two statutory heads for one statute and one direction would make a pay slip charge the
        // same contribution twice. Refused here rather than discovered on a remittance.
        if (request.Statute is { } statute
            && await _unitOfWork.Repository<PayHead>()
                .AnyAsync(new ActiveStatutoryHead(companyId, statute, request.Type), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"An active {request.Type} pay head for {statute} already exists.");
        }

        var head = new PayHead(
            companyId,
            request.Name,
            request.Type,
            request.Basis,
            request.LedgerId,
            request.Statute,
            request.SortOrder);

        await _unitOfWork.Repository<PayHead>().AddAsync(head, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(head, new Dictionary<Guid, string> { [ledger.Id] = ledger.Name });
    }

    /// <inheritdoc />
    public async Task<PayHeadSummary> SetPayHeadActiveAsync(
        Guid payHeadId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var head = await _unitOfWork.Repository<PayHead>()
            .GetByIdAsync(payHeadId, cancellationToken)
            .ConfigureAwait(false);

        if (head is null || head.CompanyId != companyId)
        {
            throw new InvalidOperationException("That pay head does not exist in this company's books.");
        }

        if (isActive)
        {
            head.Activate();
        }
        else
        {
            head.Deactivate();
        }

        _unitOfWork.Repository<PayHead>().Update(head);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var ledgers = await LedgerNamesAsync(companyId, cancellationToken).ConfigureAwait(false);

        return ToSummary(head, ledgers);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalaryStructureLineSummary>> ListStructureAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var lines = await _unitOfWork.Repository<SalaryStructureLine>()
            .ListAsync(new StructureOfEmployee(companyId, employeeId), cancellationToken)
            .ConfigureAwait(false);

        var heads = (await _unitOfWork.Repository<PayHead>()
                .ListAsync(new PayHeadsOfCompany(companyId, includeInactive: true), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(head => head.Id);

        return lines
            .OrderBy(line => line.EffectiveFrom)
            .Select(line => ToSummary(line, heads))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SalaryStructureLineSummary> AddStructureLineAsync(
        SaveSalaryStructureLineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        await RequireEmployeeAsync(companyId, request.EmployeeId, cancellationToken).ConfigureAwait(false);

        var head = await _unitOfWork.Repository<PayHead>()
            .GetByIdAsync(request.PayHeadId, cancellationToken)
            .ConfigureAwait(false);

        if (head is null || head.CompanyId != companyId)
        {
            throw new InvalidOperationException("That pay head does not exist in this company's books.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await CloseSupersededAsync(
                await _unitOfWork.Repository<SalaryStructureLine>()
                    .ListAsync(
                        new StructureOfEmployeeAndHead(companyId, request.EmployeeId, request.PayHeadId),
                        cancellationToken)
                    .ConfigureAwait(false),
                request.EffectiveFrom,
                line => line.EffectiveFrom,
                line => line.EffectiveTo,
                (line, closeOn) =>
                {
                    line.CloseAsOf(closeOn);
                    _unitOfWork.Repository<SalaryStructureLine>().Update(line);
                },
                $"a salary structure line for this employee and pay head")
            .ConfigureAwait(false);

        var created = new SalaryStructureLine(
            companyId,
            request.EmployeeId,
            request.PayHeadId,
            head.Basis,
            request.EffectiveFrom,
            request.Amount.HasValue ? MoneyScale.ToMinor(request.Amount.Value) : null,
            request.PercentMinor);

        await _unitOfWork.Repository<SalaryStructureLine>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(created, new Dictionary<Guid, PayHead> { [head.Id] = head });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatutoryRateSummary>> ListStatutoryRatesAsync(
        StatuteKind? statute = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<StatutoryRateSchedule>()
                .ListAsync(new RatesOfCompany(companyId, statute), cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(schedule => schedule.Statute)
            .ThenBy(schedule => schedule.EffectiveFrom)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<StatutoryRateSummary> AddStatutoryRateAsync(
        SaveStatutoryRateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await CloseSupersededAsync(
                await _unitOfWork.Repository<StatutoryRateSchedule>()
                    .ListAsync(new RatesOfStatute(companyId, request.Statute), cancellationToken)
                    .ConfigureAwait(false),
                request.EffectiveFrom,
                schedule => schedule.EffectiveFrom,
                schedule => schedule.EffectiveTo,
                (schedule, closeOn) =>
                {
                    schedule.CloseAsOf(closeOn);
                    _unitOfWork.Repository<StatutoryRateSchedule>().Update(schedule);
                },
                $"a {request.Statute} rate")
            .ConfigureAwait(false);

        var created = new StatutoryRateSchedule(
            companyId,
            request.Statute,
            request.EmployeeRatePercentMinor,
            request.EmployerRatePercentMinor,
            request.EffectiveFrom,
            request.WageCeiling.HasValue ? MoneyScale.ToMinor(request.WageCeiling.Value) : null,
            request.EligibilityCeiling.HasValue
                ? MoneyScale.ToMinor(request.EligibilityCeiling.Value)
                : null);

        await _unitOfWork.Repository<StatutoryRateSchedule>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(created);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfessionalTaxSlabSummary>> ListProfessionalTaxSlabsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<ProfessionalTaxSlab>()
                .ListAsync(new SlabsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(slab => slab.EffectiveFrom)
            .ThenBy(slab => slab.WageFromMinor)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ProfessionalTaxSlabSummary> AddProfessionalTaxSlabAsync(
        SaveProfessionalTaxSlabRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var from = MoneyScale.ToMinor(request.WageFrom);
        var to = request.WageTo.HasValue ? MoneyScale.ToMinor(request.WageTo.Value) : (long?)null;

        // Two bands overlapping on one date would make the tax depend on row order, which is the
        // ambiguity the exclusive upper bound exists to prevent.
        var overlapping = (await _unitOfWork.Repository<ProfessionalTaxSlab>()
                .ListAsync(new SlabsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(slab => slab.Covers(request.EffectiveFrom)
                && slab.WageFromMinor < (to ?? long.MaxValue)
                && from < (slab.WageToMinor ?? long.MaxValue));

        if (overlapping.Any())
        {
            throw new InvalidOperationException(
                "A professional tax slab already covers part of that wage band on that date.");
        }

        var created = new ProfessionalTaxSlab(
            companyId,
            from,
            to,
            MoneyScale.ToMinor(request.TaxAmount),
            request.EffectiveFrom);

        await _unitOfWork.Repository<ProfessionalTaxSlab>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(created);
    }

    /// <inheritdoc />
    public async Task<PayrollSettingsSummary> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var settings = await LoadOrCreateSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        return ToSummary(settings);
    }

    /// <inheritdoc />
    public async Task<PayrollSettingsSummary> SaveSettingsAsync(
        SavePayrollSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var settings = await LoadOrCreateSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        settings.SetSalaryPayableLedgerName(request.SalaryPayableLedgerName);
        settings.SetStatuteEnabled(StatuteKind.ProvidentFund, request.IsProvidentFundEnabled);
        settings.SetStatuteEnabled(
            StatuteKind.EmployeeStateInsurance,
            request.IsEmployeeStateInsuranceEnabled);
        settings.SetStatuteEnabled(StatuteKind.ProfessionalTax, request.IsProfessionalTaxEnabled);

        _unitOfWork.Repository<PayrollSettings>().Update(settings);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(settings);
    }

    /// <summary>
    /// Closes the open-ended row a newcomer supersedes, and refuses a genuine overlap.
    /// </summary>
    /// <remarks>
    /// The rule every dated table in this phase shares, written once. A newcomer is open-ended and
    /// owns every date from its start: a row beginning on or after that date would be swallowed by
    /// it and a closed row still running past it would overlap — both refused. An open row that
    /// began earlier is the ordinary supersession and is closed the day before.
    /// <para>
    /// Testing "does the existing row cover the new date" alone cannot separate these, because an
    /// open row covers every later date by definition. That mistake made a rate change impossible
    /// to record in the Phase 5 GST service, and this shared helper exists so the phase's five
    /// dated tables cannot each rediscover it.
    /// </para>
    /// </remarks>
    private static Task CloseSupersededAsync<T>(
        IReadOnlyList<T> existing,
        DateOnly effectiveFrom,
        Func<T, DateOnly> startOf,
        Func<T, DateOnly?> endOf,
        Action<T, DateOnly> close,
        string description)
    {
        foreach (var row in existing)
        {
            if (startOf(row) >= effectiveFrom
                || (endOf(row) is { } end && end >= effectiveFrom))
            {
                throw new InvalidOperationException(
                    $"There is already {description} in force on or after "
                    + $"{effectiveFrom:dd-MMM-yyyy}. Dated rows are added without overlapping.");
            }

            if (endOf(row) is null)
            {
                close(row, effectiveFrom.AddDays(-1));
            }
        }

        return Task.CompletedTask;
    }

    private async Task<PayrollSettings> LoadOrCreateSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<PayrollSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var created = new PayrollSettings(companyId, PayrollSeedData.SalaryPayableLedgerName);

        await _unitOfWork.Repository<PayrollSettings>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }

    private async Task<Employee> RequireEmployeeAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await _unitOfWork.Repository<Employee>()
            .GetByIdAsync(employeeId, cancellationToken)
            .ConfigureAwait(false);

        return employee is null || employee.CompanyId != companyId
            ? throw new InvalidOperationException("That employee does not exist in this company's books.")
            : employee;
    }

    private async Task<Dictionary<Guid, string>> LedgerNamesAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<Ledger>()
            .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false))
        .ToDictionary(ledger => ledger.Id, ledger => ledger.Name);

    private static EmployeeSummary ToSummary(Employee employee) =>
        new(employee.Id,
            employee.Code,
            employee.Name,
            employee.Designation,
            employee.JoinedOn,
            employee.LeftOn,
            employee.Pan,
            employee.Uan,
            employee.EsiNumber,
            employee.IsActive);

    private static PayHeadSummary ToSummary(PayHead head, Dictionary<Guid, string> ledgers) =>
        new(head.Id,
            head.Name,
            head.Type,
            head.Basis,
            head.LedgerId,
            ledgers.TryGetValue(head.LedgerId, out var name) ? name : "(unknown)",
            head.Statute,
            head.SortOrder,
            head.IsActive);

    private static SalaryStructureLineSummary ToSummary(
        SalaryStructureLine line,
        Dictionary<Guid, PayHead> heads) =>
        new(line.Id,
            line.EmployeeId,
            line.PayHeadId,
            heads.TryGetValue(line.PayHeadId, out var head) ? head.Name : "(unknown)",
            head?.Type ?? PayHeadType.Earning,
            line.Basis,
            line.AmountMinor.HasValue ? MoneyScale.FromMinor(line.AmountMinor.Value) : null,
            line.PercentMinor,
            line.EffectiveFrom,
            line.EffectiveTo);

    private static StatutoryRateSummary ToSummary(StatutoryRateSchedule schedule) =>
        new(schedule.Id,
            schedule.Statute,
            schedule.EmployeeRatePercentMinor,
            schedule.EmployerRatePercentMinor,
            schedule.WageCeilingMinor.HasValue
                ? MoneyScale.FromMinor(schedule.WageCeilingMinor.Value)
                : null,
            schedule.EligibilityCeilingMinor.HasValue
                ? MoneyScale.FromMinor(schedule.EligibilityCeilingMinor.Value)
                : null,
            schedule.EffectiveFrom,
            schedule.EffectiveTo);

    private static ProfessionalTaxSlabSummary ToSummary(ProfessionalTaxSlab slab) =>
        new(slab.Id,
            MoneyScale.FromMinor(slab.WageFromMinor),
            slab.WageToMinor.HasValue ? MoneyScale.FromMinor(slab.WageToMinor.Value) : null,
            MoneyScale.FromMinor(slab.TaxAmountMinor),
            slab.EffectiveFrom,
            slab.EffectiveTo);

    private static PayrollSettingsSummary ToSummary(PayrollSettings settings) =>
        new(settings.SalaryPayableLedgerName,
            settings.IsProvidentFundEnabled,
            settings.IsEmployeeStateInsuranceEnabled,
            settings.IsProfessionalTaxEnabled);

    private sealed class EmployeesOfCompany : Specification<Employee>
    {
        public EmployeesOfCompany(Guid companyId, bool includeInactive)
        {
            Where(employee => employee.CompanyId == companyId
                && (includeInactive || employee.IsActive));
            ApplyNoTracking();
        }
    }

    private sealed class ActiveEmployeeByCode : Specification<Employee>
    {
        public ActiveEmployeeByCode(Guid companyId, string code)
        {
            Where(employee => employee.CompanyId == companyId
                && employee.Code == code
                && employee.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class PayHeadsOfCompany : Specification<PayHead>
    {
        public PayHeadsOfCompany(Guid companyId, bool includeInactive)
        {
            Where(head => head.CompanyId == companyId && (includeInactive || head.IsActive));
            ApplyNoTracking();
        }
    }

    private sealed class ActivePayHeadByName : Specification<PayHead>
    {
        public ActivePayHeadByName(Guid companyId, string nameNormalised)
        {
            Where(head => head.CompanyId == companyId
                && head.NameNormalised == nameNormalised
                && head.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class ActiveStatutoryHead : Specification<PayHead>
    {
        public ActiveStatutoryHead(Guid companyId, StatuteKind statute, PayHeadType type)
        {
            Where(head => head.CompanyId == companyId
                && head.Statute == statute
                && head.Type == type
                && head.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class StructureOfEmployee : Specification<SalaryStructureLine>
    {
        public StructureOfEmployee(Guid companyId, Guid employeeId)
        {
            Where(line => line.CompanyId == companyId && line.EmployeeId == employeeId);
            ApplyNoTracking();
        }
    }

    private sealed class StructureOfEmployeeAndHead : Specification<SalaryStructureLine>
    {
        public StructureOfEmployeeAndHead(Guid companyId, Guid employeeId, Guid payHeadId)
        {
            Where(line => line.CompanyId == companyId
                && line.EmployeeId == employeeId
                && line.PayHeadId == payHeadId);
        }
    }

    private sealed class RatesOfCompany : Specification<StatutoryRateSchedule>
    {
        public RatesOfCompany(Guid companyId, StatuteKind? statute)
        {
            Where(schedule => schedule.CompanyId == companyId
                && (!statute.HasValue || schedule.Statute == statute.Value));
            ApplyNoTracking();
        }
    }

    private sealed class RatesOfStatute : Specification<StatutoryRateSchedule>
    {
        public RatesOfStatute(Guid companyId, StatuteKind statute)
        {
            Where(schedule => schedule.CompanyId == companyId && schedule.Statute == statute);
        }
    }

    private sealed class SlabsOfCompany : Specification<ProfessionalTaxSlab>
    {
        public SlabsOfCompany(Guid companyId)
        {
            Where(slab => slab.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class SettingsOfCompany : Specification<PayrollSettings>
    {
        public SettingsOfCompany(Guid companyId) => Where(settings => settings.CompanyId == companyId);
    }

    private sealed class LedgersOfCompany : Specification<Ledger>
    {
        public LedgersOfCompany(Guid companyId)
        {
            Where(ledger => ledger.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
