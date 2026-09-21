using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Application.Payroll;

/// <summary>
/// Computes and posts a month's payroll (phase-7-tds-payroll-banking.md §4a D3, checkpoint C7.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>The statutory flags gate the code path, not the figure.</b> Checkpoint C7.7 is not "with the
/// flags off the contributions come out zero" — it is that no statutory row is produced and no
/// statutory code path runs. So the engine filters the statutory heads by
/// <c>PayrollSettings.IsEnabled</c> <em>before</em> it reads anything, and with every flag off it
/// never touches a rate table, never calls <c>PayrollMath.Contribution</c>, and emits no line. A
/// zero line would claim the scheme applies and happened to cost nothing, which is false.
/// </para>
/// <para>
/// <b>Order of computation is load-bearing.</b> Basic is needed before a percent-of-basic head can
/// resolve; gross is needed before a percent-of-gross head or any statutory contribution can. So
/// earnings settle first, gross is totalled, and only then do deductions and contributions
/// compute — which is also the order a pay slip reads in.
/// </para>
/// <para>
/// Posting composes the Phase 1 journal engine exactly as <c>StockVoucherService</c> does: the
/// salary voucher is the document, the journal voucher is the accounting, and they commit together
/// or not at all.
/// </para>
/// </remarks>
public sealed class PayrollRunService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    IVoucherPostingService voucherPostingService) : IPayrollRunService
{
    /// <summary>The name a pay head must carry for the engine to treat it as basic pay.</summary>
    /// <remarks>
    /// The one name the engine knows, and it knows it because <c>PercentOfBasic</c> is meaningless
    /// without an agreed answer to "which head is basic". Stated as a constant here rather than as
    /// a flag on the head only because a company has exactly one basic; if that ever stops being
    /// true it becomes a column, not a second constant.
    /// </remarks>
    public const string BasicPayHeadName = "Basic";

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IVoucherPostingService _voucherPostingService = voucherPostingService
        ?? throw new ArgumentNullException(nameof(voucherPostingService));

    /// <inheritdoc />
    public async Task<PayrollPreview> PreviewAsync(
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return await ComputeAsync(companyId, periodEnd, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PostedPayrollSummary> PostAsync(
        DateOnly periodEnd,
        Guid voucherTypeId,
        string? narration = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        if (await _unitOfWork.Repository<SalaryVoucher>()
                .AnyAsync(new RunForPeriod(companyId, periodEnd), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Payroll for {periodEnd:MMMM yyyy} has already been posted. Cancel that run "
                + "before posting another.");
        }

        var preview = await ComputeAsync(companyId, periodEnd, cancellationToken).ConfigureAwait(false);

        if (preview.PaySlips.Count == 0)
        {
            throw new InvalidOperationException(
                $"No employee was on the payroll in {periodEnd:MMMM yyyy}, so there is nothing to post.");
        }

        var heads = await PayHeadsAsync(companyId, cancellationToken).ConfigureAwait(false);
        var settings = await SettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var salaryVoucher = new SalaryVoucher(companyId, periodEnd, narration);

        foreach (var slip in preview.PaySlips)
        {
            foreach (var line in slip.Lines)
            {
                var head = heads[line.PayHeadId];

                salaryVoucher.AddLine(
                    slip.EmployeeId,
                    head.Id,
                    head.LedgerId,
                    head.Name,
                    head.Type,
                    MoneyScale.ToMinor(line.Amount),
                    head.Statute,
                    head.SortOrder);
            }
        }

        var salaryPayableLedgerId = await RequireLedgerIdByNameAsync(
                companyId,
                settings.SalaryPayableLedgerName,
                cancellationToken)
            .ConfigureAwait(false);

        var posted = await _voucherPostingService.PostAsync(
                new PostVoucherRequest(
                    voucherTypeId,
                    periodEnd,
                    BuildJournalLines(salaryVoucher, heads, salaryPayableLedgerId),
                    narration ?? $"Payroll for {periodEnd:MMMM yyyy}"),
                cancellationToken)
            .ConfigureAwait(false);

        salaryVoucher.AttachPostedVoucher(posted.VoucherId, posted.Number);

        await _unitOfWork.Repository<SalaryVoucher>().AddAsync(salaryVoucher, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Summarise(salaryVoucher);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostedPayrollSummary>> ListRunsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<SalaryVoucher>()
                .ListAsync(new RunsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(run => run.PeriodEnd)
            .Select(Summarise)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PaySlip?> GetPaySlipAsync(
        Guid salaryVoucherId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var run = await _unitOfWork.Repository<SalaryVoucher>()
            .SingleOrDefaultAsync(new RunById(companyId, salaryVoucherId), cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        var lines = run.LinesFor(employeeId);

        if (lines.Count == 0)
        {
            return null;
        }

        var employee = await _unitOfWork.Repository<Employee>()
            .GetByIdAsync(employeeId, cancellationToken)
            .ConfigureAwait(false);

        return BuildSlip(
            employeeId,
            employee?.Code ?? "(unknown)",
            employee?.Name ?? "(unknown)",
            lines.Select(line => new PaySlipLine(
                    line.PayHeadId,
                    line.PayHeadName,
                    line.Type,
                    MoneyScale.FromMinor(line.AmountMinor),
                    line.Statute,
                    line.SortOrder))
                .ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatutoryChallanLine>> ChallanSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var runIds = (await _unitOfWork.Repository<SalaryVoucher>()
                .ListAsync(new RunsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(run => run.PeriodEnd >= fromDate && run.PeriodEnd <= toDate)
            .Select(run => run.Id)
            .ToHashSet();

        if (runIds.Count == 0)
        {
            return [];
        }

        var lines = (await _unitOfWork.Repository<SalaryVoucherLine>()
                .ListAsync(new LinesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(line => runIds.Contains(line.SalaryVoucherId) && line.Statute.HasValue)
            .ToList();

        // No statutory lines means no statute is in force, and an empty list is the honest answer.
        // Three zero rows would each claim a scheme applies (C7.7).
        return lines
            .GroupBy(line => line.Statute!.Value)
            .Select(group => new StatutoryChallanLine(
                group.Key,
                group.Select(line => line.EmployeeId).Distinct().Count(),
                MoneyScale.FromMinor(group
                    .Where(line => line.Type == PayHeadType.Deduction)
                    .Sum(line => line.AmountMinor)),
                MoneyScale.FromMinor(group
                    .Where(line => line.Type == PayHeadType.EmployerContribution)
                    .Sum(line => line.AmountMinor)),
                MoneyScale.FromMinor(group.Sum(line => line.AmountMinor))))
            .OrderBy(line => line.Statute)
            .ToList();
    }

    /// <summary>
    /// Computes every employee's slip for the month, reading only what is in force on that date.
    /// </summary>
    private async Task<PayrollPreview> ComputeAsync(
        Guid companyId,
        DateOnly periodEnd,
        CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(companyId, cancellationToken).ConfigureAwait(false);
        var heads = await PayHeadsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var employees = (await _unitOfWork.Repository<Employee>()
                .ListAsync(new EmployeesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(employee => employee.IsOnRollOn(periodEnd))
            .OrderBy(employee => employee.Code, StringComparer.Ordinal)
            .ToList();

        var structure = (await _unitOfWork.Repository<SalaryStructureLine>()
                .ListAsync(new StructureOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(line => line.Covers(periodEnd))
            .ToLookup(line => line.EmployeeId);

        // Read once for the whole run rather than per employee — and only for statutes actually in
        // force, so that with every flag off these two reads never happen at all (C7.7).
        var rates = settings.HasNoStatutesInForce
            ? []
            : (await _unitOfWork.Repository<StatutoryRateSchedule>()
                    .ListAsync(new RatesOfCompany(companyId), cancellationToken)
                    .ConfigureAwait(false))
                .Where(rate => rate.Covers(periodEnd) && settings.IsEnabled(rate.Statute))
                .ToDictionary(rate => rate.Statute);

        var slabs = settings.IsProfessionalTaxEnabled
            ? (await _unitOfWork.Repository<ProfessionalTaxSlab>()
                    .ListAsync(new SlabsOfCompany(companyId), cancellationToken)
                    .ConfigureAwait(false))
                .Where(slab => slab.Covers(periodEnd))
                .OrderBy(slab => slab.WageFromMinor)
                .ToList()
            : [];

        var slips = new List<PaySlip>(employees.Count);
        var warnings = new List<string>();

        foreach (var employee in employees)
        {
            var lines = structure[employee.Id].ToList();

            if (lines.Count == 0)
            {
                warnings.Add(
                    $"{employee.Code} {employee.Name} has no salary structure in force on "
                    + $"{periodEnd:dd-MMM-yyyy} and was skipped.");
                continue;
            }

            slips.Add(ComputeSlip(employee, lines, heads, settings, rates, slabs, warnings));
        }

        return new PayrollPreview(
            new DateOnly(periodEnd.Year, periodEnd.Month, 1),
            periodEnd,
            slips.Sum(slip => slip.GrossEarnings),
            slips.Sum(slip => slip.TotalDeductions),
            slips.Sum(slip => slip.EmployerContributions),
            slips.Sum(slip => slip.NetPay),
            slips,
            warnings);
    }

    /// <summary>One employee's slip: earnings, then gross, then deductions and contributions.</summary>
    private static PaySlip ComputeSlip(
        Employee employee,
        IReadOnlyList<SalaryStructureLine> structure,
        Dictionary<Guid, PayHead> heads,
        PayrollSettings settings,
        Dictionary<StatuteKind, StatutoryRateSchedule> rates,
        IReadOnlyList<ProfessionalTaxSlab> slabs,
        List<string> warnings)
    {
        var lines = new List<PaySlipLine>();

        var structured = structure
            .Where(line => heads.ContainsKey(line.PayHeadId))
            .Select(line => (Line: line, Head: heads[line.PayHeadId]))
            .ToList();

        // Basic first: every percent-of-basic head depends on it, so it cannot be resolved in the
        // same pass as the heads that consume it.
        var basicMinor = structured
            .Where(pair => pair.Head.Type == PayHeadType.Earning
                && pair.Head.Basis == PayComputationBasis.FlatAmount
                && string.Equals(pair.Head.Name, BasicPayHeadName, StringComparison.OrdinalIgnoreCase))
            .Sum(pair => pair.Line.AmountMinor ?? 0L);

        var earnings = new List<(PayHead Head, long Minor)>();

        foreach (var (line, head) in structured.Where(pair => pair.Head.Type == PayHeadType.Earning))
        {
            if (head.IsStatutory)
            {
                continue;
            }

            earnings.Add((head, PayrollMath.HeadAmount(
                line.Basis,
                line.AmountMinor,
                line.PercentMinor,
                basicMinor,

                // Gross is not yet known, and a percent-of-gross *earning* would be circular. Zero
                // is passed deliberately, and the structure screen does not offer the combination.
                grossMinor: 0L)));
        }

        var grossMinor = earnings.Sum(pair => pair.Minor);

        lines.AddRange(earnings.Select(pair => ToLine(pair.Head, pair.Minor)));

        // Non-statutory deductions, which may be a percentage of the gross just computed.
        foreach (var (line, head) in structured
                     .Where(pair => pair.Head.Type == PayHeadType.Deduction && !pair.Head.IsStatutory))
        {
            lines.Add(ToLine(
                head,
                PayrollMath.HeadAmount(
                    line.Basis, line.AmountMinor, line.PercentMinor, basicMinor, grossMinor)));
        }

        // The statutory pass. Filtered by the flag before anything is read or computed: with every
        // flag off this loop produces nothing and PayrollMath is never called (C7.7).
        foreach (var head in heads.Values
                     .Where(head => head.IsActive && head.IsStatutory)
                     .Where(head => head.Statute is { } statute && settings.IsEnabled(statute))
                     .OrderBy(head => head.SortOrder))
        {
            var statute = head.Statute!.Value;

            var amountMinor = statute == StatuteKind.ProfessionalTax
                ? ProfessionalTaxFor(head, grossMinor, slabs)
                : ContributionFor(head, statute, grossMinor, rates, employee, warnings);

            if (amountMinor > 0L)
            {
                lines.Add(ToLine(head, amountMinor));
            }
        }

        return BuildSlip(employee.Id, employee.Code, employee.Name, lines);
    }

    private static long ProfessionalTaxFor(
        PayHead head,
        long grossMinor,
        IReadOnlyList<ProfessionalTaxSlab> slabs) =>
        head.Type == PayHeadType.Deduction
            ? PayrollMath.ProfessionalTax(
                grossMinor,
                slabs.Select(slab => (slab.WageFromMinor, slab.WageToMinor, slab.TaxAmountMinor)))
            : 0L;

    private static long ContributionFor(
        PayHead head,
        StatuteKind statute,
        long grossMinor,
        Dictionary<StatuteKind, StatutoryRateSchedule> rates,
        Employee employee,
        List<string> warnings)
    {
        if (!rates.TryGetValue(statute, out var rate))
        {
            // The flag is on and no dated row covers the month. Silence here would under-remit for
            // the whole period, so it surfaces as a warning the owner can act on.
            warnings.Add(
                $"{statute} is switched on but no contribution rate is in force for this month, "
                + $"so nothing was computed for {employee.Code}.");
            return 0L;
        }

        var contribution = PayrollMath.Contribution(
            grossMinor,
            rate.EmployeeRatePercentMinor,
            rate.EmployerRatePercentMinor,
            rate.WageCeilingMinor,
            rate.EligibilityCeilingMinor);

        return head.Type switch
        {
            PayHeadType.Deduction => contribution.EmployeeShareMinor,
            PayHeadType.EmployerContribution => contribution.EmployerShareMinor,
            _ => 0L,
        };
    }

    /// <summary>
    /// The journal lines a run posts: expense debits, liability credits, net pay to the payable.
    /// </summary>
    /// <remarks>
    /// Employer contributions are both a debit and a credit — an expense of the company and a
    /// liability to the fund — and never touch net pay. That is the whole reason
    /// <see cref="PayHeadType.EmployerContribution"/> exists as a third type rather than being
    /// folded into one of the other two.
    /// </remarks>
    private static List<PostVoucherLine> BuildJournalLines(
        SalaryVoucher run,
        Dictionary<Guid, PayHead> heads,
        Guid salaryPayableLedgerId)
    {
        var lines = new List<PostVoucherLine>();

        foreach (var group in run.Lines.GroupBy(line => (line.LedgerId, line.Type)))
        {
            var amount = MoneyScale.FromMinor(group.Sum(line => line.AmountMinor));

            if (amount == 0m)
            {
                continue;
            }

            lines.Add(group.Key.Type switch
            {
                // Gross earnings are the company's expense.
                PayHeadType.Earning => new PostVoucherLine(group.Key.LedgerId, amount, 0m),

                // A deduction is withheld from the employee and owed to somebody else.
                PayHeadType.Deduction => new PostVoucherLine(group.Key.LedgerId, 0m, amount),

                // The employer's own share: an expense on the head the pay head names.
                _ => new PostVoucherLine(group.Key.LedgerId, amount, 0m),
            });
        }

        // The employer's contributions are also a liability, and the pay head's own ledger carried
        // only the expense side. The matching credit goes to the same statutory payable the
        // employee share went to, which is what makes the challan figure the sum of both.
        foreach (var group in run.Lines
                     .Where(line => line.Type == PayHeadType.EmployerContribution)
                     .GroupBy(line => line.Statute))
        {
            var amount = MoneyScale.FromMinor(group.Sum(line => line.AmountMinor));

            if (amount == 0m || group.Key is not { } statute)
            {
                continue;
            }

            var payable = heads.Values.FirstOrDefault(head =>
                head.Statute == statute && head.Type == PayHeadType.Deduction);

            if (payable is not null)
            {
                lines.Add(new PostVoucherLine(payable.LedgerId, 0m, amount));
            }
        }

        var netPay = MoneyScale.FromMinor(run.NetPayableMinor);

        if (netPay != 0m)
        {
            // What the employees are actually owed, credited to the salary payable liability until
            // it is paid out. Gross went to expense and the deductions to their own liabilities;
            // this is the remainder, and it is what makes the voucher balance.
            lines.Add(new PostVoucherLine(salaryPayableLedgerId, 0m, netPay));
        }

        return lines;
    }

    private static PaySlip BuildSlip(
        Guid employeeId,
        string code,
        string name,
        IReadOnlyList<PaySlipLine> lines)
    {
        var gross = lines.Where(line => line.Type == PayHeadType.Earning).Sum(line => line.Amount);
        var deductions = lines.Where(line => line.Type == PayHeadType.Deduction).Sum(line => line.Amount);
        var employer = lines
            .Where(line => line.Type == PayHeadType.EmployerContribution)
            .Sum(line => line.Amount);

        return new PaySlip(
            employeeId,
            code,
            name,
            gross,
            deductions,
            employer,
            gross - deductions,
            lines.OrderBy(line => line.Type).ThenBy(line => line.SortOrder).ToList());
    }

    private static PaySlipLine ToLine(PayHead head, long amountMinor) =>
        new(head.Id,
            head.Name,
            head.Type,
            MoneyScale.FromMinor(amountMinor),
            head.Statute,
            head.SortOrder);

    private static PostedPayrollSummary Summarise(SalaryVoucher run) =>
        new(run.Id,
            run.PostedVoucherId,
            run.PostedVoucherNumber,
            run.PeriodStart,
            run.PeriodEnd,
            run.EmployeeCount,
            MoneyScale.FromMinor(run.GrossEarningsMinor),
            MoneyScale.FromMinor(run.TotalDeductionsMinor),
            MoneyScale.FromMinor(run.EmployerContributionsMinor),
            MoneyScale.FromMinor(run.NetPayableMinor));

    private async Task<PayrollSettings> SettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<PayrollSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException(
            "Payroll settings have not been configured for this company.");

    private async Task<Guid> RequireLedgerIdByNameAsync(
        Guid companyId,
        string ledgerName,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(ledgerName);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .SingleOrDefaultAsync(new LedgerByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        return ledger?.Id
            ?? throw new InvalidOperationException(
                $"Payroll settings name '{ledgerName}' as the salary payable ledger, but no such "
                + "ledger exists. Create it, or point the setting at one that does.");
    }

    private async Task<Dictionary<Guid, PayHead>> PayHeadsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<PayHead>()
            .ListAsync(new PayHeadsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false))
        .ToDictionary(head => head.Id);

    private sealed class EmployeesOfCompany : Specification<Employee>
    {
        public EmployeesOfCompany(Guid companyId)
        {
            Where(employee => employee.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class PayHeadsOfCompany : Specification<PayHead>
    {
        public PayHeadsOfCompany(Guid companyId)
        {
            Where(head => head.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class StructureOfCompany : Specification<SalaryStructureLine>
    {
        public StructureOfCompany(Guid companyId)
        {
            Where(line => line.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class RatesOfCompany : Specification<StatutoryRateSchedule>
    {
        public RatesOfCompany(Guid companyId)
        {
            Where(schedule => schedule.CompanyId == companyId);
            ApplyNoTracking();
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
        public SettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class RunsOfCompany : Specification<SalaryVoucher>
    {
        public RunsOfCompany(Guid companyId)
        {
            Where(run => run.CompanyId == companyId);
            AddInclude(run => run.Lines);
            ApplyNoTracking();
        }
    }

    private sealed class RunById : Specification<SalaryVoucher>
    {
        public RunById(Guid companyId, Guid id)
        {
            Where(run => run.CompanyId == companyId && run.Id == id);
            AddInclude(run => run.Lines);
            ApplyNoTracking();
        }
    }

    private sealed class RunForPeriod : Specification<SalaryVoucher>
    {
        public RunForPeriod(Guid companyId, DateOnly periodEnd)
        {
            Where(run => run.CompanyId == companyId && run.PeriodEnd == periodEnd);
            ApplyNoTracking();
        }
    }

    private sealed class LinesOfCompany : Specification<SalaryVoucherLine>
    {
        public LinesOfCompany(Guid companyId)
        {
            Where(line => line.CompanyId == companyId);
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
