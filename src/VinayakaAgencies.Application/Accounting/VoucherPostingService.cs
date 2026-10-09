using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// The journal engine: the only sanctioned way a voucher reaches the books (spec §8).
/// </summary>
/// <remarks>
/// Posting validates everything first and allocates a number only afterwards, so a rejected voucher
/// never consumes one (§7.2). Alteration and cancellation follow §8.2's five steps exactly —
/// validate, open a transaction, save the supersession alone, then save the successor with its
/// lines, then commit — because between saves three and four the voucher has no current revision:
/// its number is free and it is invisible to every derived balance. That state must never be
/// committed, which is why this procedure is one method here rather than a sequence a caller could
/// reassemble.
/// <para>
/// A cancelled voucher keeps its number: cancellation is a new revision carrying no lines, so every
/// report excludes it automatically while the filtered unique index still holds the number against
/// reuse (§8.6, decision D2).
/// </para>
/// </remarks>
public sealed class VoucherPostingService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    IVoucherNumberAllocator numberAllocator,
    IPermissionAuthority permissions) : IVoucherPostingService
{
    /// <summary>The seeded groups whose subtree defines "cash or bank" for the Contra rule.</summary>
    /// <remarks>
    /// Held already-normalised, because <c>AccountGroup.NameNormalised</c> is what they are matched
    /// against and <see cref="NameNormaliser"/> upper-cases it. Comparing the display casing
    /// ordinally — as this did until it was caught by the C1.5 fixture — matches no group at all,
    /// which silently made every Contra voucher invalid rather than restricting it to cash and bank.
    /// </remarks>
    private static readonly HashSet<string> CashAndBankGroupNames = new(
        new[] { "Cash-in-Hand", "Bank Accounts", "Bank OD A/c" }.Select(NameNormaliser.Normalise),
        StringComparer.Ordinal);

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IVoucherNumberAllocator _numberAllocator = numberAllocator
        ?? throw new ArgumentNullException(nameof(numberAllocator));

    // Phase 9: posting, altering and cancelling each have their own permission (§4a D4 refuses the
    // Operator role cancellation specifically). Required rather than optional, like the audit
    // trail's dependencies, so this service cannot be composed without its gate.
    private readonly IPermissionAuthority _permissions = permissions
        ?? throw new ArgumentNullException(nameof(permissions));

    /// <inheritdoc />
    public async Task<IReadOnlyList<VoucherTypeSummary>> ListVoucherTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var types = await _unitOfWork.Repository<VoucherType>()
            .ListAsync(new ActiveVoucherTypes(companyId), cancellationToken)
            .ConfigureAwait(false);

        return types
            .OrderBy(type => type.NameNormalised, StringComparer.Ordinal)
            .Select(type => new VoucherTypeSummary(
                type.Id, type.Name, type.Kind, type.DefaultSeriesId, type.IsActive))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PostedVoucherSummary> PostAsync(
        PostVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _permissions.EnsureAsync(PermissionKind.PostVoucher, cancellationToken)
            .ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var voucherType = await RequireActiveVoucherTypeAsync(companyId, request.VoucherTypeId, cancellationToken)
            .ConfigureAwait(false);

        // Step 1 — validate everything before anything is written. A rejected voucher must not have
        // consumed a number, created a financial year or left a partial graph behind.
        var resolved = await ValidateProposedContentAsync(
            companyId,
            voucherType.Kind,
            request.VoucherDate,
            request.Lines,
            cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Allocation sits inside the transaction (spec §7.2): on a first post into a new financial
        // year the year row, the sequence row and the voucher must land together or not at all.
        var allocated = await _numberAllocator.AllocateAsync(
            companyId,
            request.VoucherTypeId,
            request.VoucherDate,
            request.ManualNumber,
            cancellationToken)
            .ConfigureAwait(false);

        var voucher = new Voucher(companyId, voucherType.Id, allocated.SeriesId, allocated.FinancialYearId);
        var revision = new VoucherRevision(
            voucher,
            revisionNumber: 1,
            voucherDate: request.VoucherDate,
            number: allocated.Number,
            isManualNumber: allocated.IsManual,
            narration: request.Narration,
            referenceNumber: request.ReferenceNumber,
            referenceDate: request.ReferenceDate);

        foreach (var line in resolved.Lines)
        {
            revision.AddLine(new JournalLine(
                companyId,
                voucher.Id,
                revision.Id,
                line.LineNumber,
                line.Ledger.Id,
                line.Amount,
                costCentreId: line.CostCentreId,
                narration: line.Narration));
        }

        await _unitOfWork.Repository<Voucher>().AddAsync(voucher, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.Repository<VoucherRevision>()
            .AddAsync(revision, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Summarise(revision, voucherType);
    }

    /// <inheritdoc />
    public async Task<PostedVoucherSummary> AlterAsync(
        AlterVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ChangeReason);

        await _permissions.EnsureAsync(PermissionKind.AlterVoucher, cancellationToken)
            .ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var (voucher, predecessor) = await RequireCurrentRevisionAsync(request.VoucherId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (predecessor.Status == VoucherStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Voucher {predecessor.Number} is cancelled and cannot be altered. Its history is closed.");
        }

        var voucherType = await RequireActiveVoucherTypeAsync(companyId, voucher.VoucherTypeId, cancellationToken)
            .ConfigureAwait(false);

        // Step 1 — validate against the proposed revision. The change reason is mandatory on every
        // revision after the first; the number is carried forward untouched, so no allocation runs.
        var resolved = await ValidateProposedContentAsync(
            companyId,
            voucherType.Kind,
            request.VoucherDate,
            request.Lines,
            cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var successor = new VoucherRevision(
            voucher,
            revisionNumber: predecessor.RevisionNumber + 1,
            voucherDate: request.VoucherDate,
            number: predecessor.Number,
            isManualNumber: predecessor.IsManualNumber,
            narration: request.Narration,
            referenceNumber: request.ReferenceNumber,
            referenceDate: request.ReferenceDate,
            changeReason: request.ChangeReason);

        foreach (var line in resolved.Lines)
        {
            successor.AddLine(new JournalLine(
                companyId,
                voucher.Id,
                successor.Id,
                line.LineNumber,
                line.Ledger.Id,
                line.Amount,
                costCentreId: line.CostCentreId,
                narration: line.Narration));
        }

        // Steps 3 and 4 — two separate saves, in this order, inside one transaction. The successor's
        // id exists before insert (client-generated, ADR-0001), so the predecessor can leave the
        // filtered uniqueness index before the successor claims the same number in it.
        predecessor.SupersedeBy(successor.Id);
        _unitOfWork.Repository<VoucherRevision>().Update(predecessor);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _unitOfWork.Repository<VoucherRevision>()
            .AddAsync(successor, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Summarise(successor, voucherType);
    }

    /// <inheritdoc />
    public async Task<PostedVoucherSummary> CancelAsync(
        Guid voucherId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _permissions.EnsureAsync(PermissionKind.CancelVoucher, cancellationToken)
            .ConfigureAwait(false);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var (voucher, predecessor) = await RequireCurrentRevisionAsync(voucherId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (predecessor.Status == VoucherStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Voucher {predecessor.Number} is already cancelled.");
        }

        var voucherType = await RequireActiveVoucherTypeAsync(companyId, voucher.VoucherTypeId, cancellationToken)
            .ConfigureAwait(false);

        // Cancellation does not move the date, but it is still a posting-affecting change, so the
        // period lock applies to the date it would stamp (§12).
        await EnsureDateOpenAsync(companyId, predecessor.VoucherDate, cancellationToken).ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // An empty revision: trivially balanced, excluded from every derived figure by construction,
        // and still holding the number so the series shows no reusable gap (§8.6).
        var successor = new VoucherRevision(
            voucher,
            revisionNumber: predecessor.RevisionNumber + 1,
            voucherDate: predecessor.VoucherDate,
            number: predecessor.Number,
            isManualNumber: predecessor.IsManualNumber,
            changeReason: reason.Trim(),
            status: VoucherStatus.Cancelled);

        predecessor.SupersedeBy(successor.Id);
        _unitOfWork.Repository<VoucherRevision>().Update(predecessor);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _unitOfWork.Repository<VoucherRevision>()
            .AddAsync(successor, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Summarise(successor, voucherType);
    }

    /// <inheritdoc />
    public async Task<PostedVoucherSummary?> GetCurrentAsync(
        Guid voucherId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var revision = await _unitOfWork.Repository<VoucherRevision>()
            .SingleOrDefaultAsync(new CurrentRevisionOf(voucherId), cancellationToken)
            .ConfigureAwait(false);

        if (revision is null || revision.CompanyId != companyId)
        {
            return null;
        }

        var voucherType = await _unitOfWork.Repository<VoucherType>()
            .GetByIdAsync(revision.VoucherTypeId, cancellationToken)
            .ConfigureAwait(false);

        return Summarise(
            revision,
            voucherType ?? throw new InvalidOperationException(
                $"Voucher {revision.Number} names voucher type '{revision.VoucherTypeId}', which does not exist."));
    }

    /// <summary>
    /// Every rule of §8.1 that can be checked without writing: line shapes, ledger eligibility, the
    /// period lock, an open financial year, the Contra restriction, and the exact balance.
    /// </summary>
    private async Task<ResolvedContent> ValidateProposedContentAsync(
        Guid companyId,
        VoucherKind kind,
        DateOnly voucherDate,
        IReadOnlyList<PostVoucherLine> lines,
        CancellationToken cancellationToken)
    {
        var numbered = ValidateLineShapes(lines);

        var ledgers = await ResolveLedgersAsync(companyId, numbered, cancellationToken).ConfigureAwait(false);
        await EnsureDateOpenAsync(companyId, voucherDate, cancellationToken).ConfigureAwait(false);

        if (kind == VoucherKind.Contra)
        {
            await EnsureContraAllowedAsync(companyId, ledgers, cancellationToken).ConfigureAwait(false);
        }

        var totalMinor = numbered.Sum(line =>
            MoneyScale.ToMinor(line.Debit) - MoneyScale.ToMinor(line.Credit));

        if (totalMinor != 0)
        {
            throw new InvalidOperationException(
                $"Debits and credits do not balance. The voucher is out by " +
                $"{MoneyScale.ToInvariantString(MoneyScale.FromMinor(Math.Abs(totalMinor)))} " +
                "(debits minus credits must be exactly zero).");
        }

        var content = new List<ResolvedLine>(numbered.Count);

        for (var index = 0; index < numbered.Count; index++)
        {
            var line = numbered[index];
            var amount = line.Debit > 0m ? line.Debit : -line.Credit;

            content.Add(new ResolvedLine(
                index + 1,
                ledgers[line.LedgerId],
                amount,
                line.Narration,
                line.CostCentreId));
        }

        return new ResolvedContent(content);
    }

    /// <summary>
    /// Two-column presentation rules: at least two lines, exactly one side non-zero per line, and at
    /// least one debit and one credit overall (§8.1).
    /// </summary>
    private static IReadOnlyList<PostVoucherLine> ValidateLineShapes(IReadOnlyList<PostVoucherLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count < 2)
        {
            throw new InvalidOperationException(
                "A voucher needs at least two lines — one debit and one credit.");
        }

        foreach (var line in lines)
        {
            if (line.Debit < 0m || line.Credit < 0m)
            {
                throw new InvalidOperationException(
                    "A voucher line carries a negative amount. Enter debits and credits as positive " +
                    "figures in their own column.");
            }

            if (line.Debit == 0m && line.Credit == 0m)
            {
                throw new InvalidOperationException(
                    "A voucher line carries nothing. Enter an amount on the debit or the credit side.");
            }

            if (line.Debit > 0m && line.Credit > 0m)
            {
                throw new InvalidOperationException(
                    "A voucher line carries both a debit and a credit. Put each amount on one side only.");
            }
        }

        if (!lines.Any(line => line.Debit > 0m))
        {
            throw new InvalidOperationException("A voucher needs at least one debit line.");
        }

        if (!lines.Any(line => line.Credit > 0m))
        {
            throw new InvalidOperationException("A voucher needs at least one credit line.");
        }

        return lines;
    }

    /// <summary>
    /// Every named ledger must exist for this company, be active, and permit manual posting (§8.1) —
    /// which is what keeps the derived Profit &amp; Loss A/c figure from ever being posted to.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Ledger>> ResolveLedgersAsync(
        Guid companyId,
        IReadOnlyList<PostVoucherLine> lines,
        CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<Ledger>();
        var ledgerIds = lines.Select(line => line.LedgerId).Distinct().ToList();

        var found = await repository
            .ListAsync(new LedgersByIds(ledgerIds), cancellationToken)
            .ConfigureAwait(false);

        var byId = found.ToDictionary(ledger => ledger.Id);

        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.LedgerId, out var ledger))
            {
                throw new InvalidOperationException($"Ledger '{line.LedgerId}' does not exist.");
            }

            if (ledger.CompanyId != companyId)
            {
                throw new InvalidOperationException(
                    $"'{ledger.Name}' belongs to another company's books. Cross-company posting is not allowed.");
            }

            if (!ledger.IsActive)
            {
                throw new InvalidOperationException(
                    $"'{ledger.Name}' is inactive. Reactivate it or post to another ledger.");
            }

            if (!ledger.AllowsManualPosting)
            {
                throw new InvalidOperationException(
                    $"'{ledger.Name}' does not accept manual postings; its balance is derived.");
            }
        }

        return byId;
    }

    /// <summary>
    /// The business date must fall after the period lock and inside an open financial year (§12).
    /// </summary>
    private async Task EnsureDateOpenAsync(
        Guid companyId,
        DateOnly voucherDate,
        CancellationToken cancellationToken)
    {
        var periodLock = await _unitOfWork.Repository<PeriodLock>()
            .SingleOrDefaultAsync(new PeriodLockOf(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (periodLock?.IsLocked(voucherDate) == true)
        {
            throw new InvalidOperationException(
                $"The books are locked through {periodLock.LockedThroughDate:dd-MMM-yyyy}. " +
                $"A voucher dated {voucherDate:dd-MMM-yyyy} cannot be posted into that period.");
        }

        var closedYear = await _unitOfWork.Repository<FinancialYear>()
            .AnyAsync(new ClosedYearContaining(companyId, voucherDate), cancellationToken)
            .ConfigureAwait(false);

        if (closedYear)
        {
            throw new InvalidOperationException(
                $"Financial year containing {voucherDate:dd-MMM-yyyy} is closed to further posting.");
        }
    }

    /// <summary>
    /// Contra vouchers move value only within cash and bank. Whether a ledger qualifies is *derived*
    /// from its group's position in the chart — at or under Cash-in-Hand, Bank Accounts or Bank OD
    /// A/c — never stored on the ledger itself.
    /// </summary>
    private async Task EnsureContraAllowedAsync(
        Guid companyId,
        IReadOnlyDictionary<Guid, Ledger> ledgers,
        CancellationToken cancellationToken)
    {
        var groups = await _unitOfWork.Repository<AccountGroup>()
            .ListAsync(new GroupsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var groupsById = groups.ToDictionary(group => group.Id);

        var cashAndBankPaths = groups
            .Where(group => CashAndBankGroupNames.Contains(group.NameNormalised))
            .Select(group => group.MaterialisedPath)
            .ToList();

        foreach (var ledger in ledgers.Values.Distinct())
        {
            if (!groupsById.TryGetValue(ledger.AccountGroupId, out var group))
            {
                throw new InvalidOperationException(
                    $"'{ledger.Name}' points at account group '{ledger.AccountGroupId}', which does not exist.");
            }

            var underCashOrBank = cashAndBankPaths.Any(path =>
                group.MaterialisedPath.StartsWith(path, StringComparison.Ordinal));

            if (!underCashOrBank)
            {
                throw new InvalidOperationException(
                    $"'{ledger.Name}' is under '{group.Name}'. A Contra voucher may only move value " +
                    "between cash and bank ledgers.");
            }
        }
    }

    private async Task<VoucherType> RequireActiveVoucherTypeAsync(
        Guid companyId,
        Guid voucherTypeId,
        CancellationToken cancellationToken)
    {
        var voucherType = await _unitOfWork.Repository<VoucherType>()
            .GetByIdAsync(voucherTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (voucherType is null || voucherType.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Voucher type '{voucherTypeId}' does not exist.");
        }

        if (!voucherType.IsActive)
        {
            throw new InvalidOperationException(
                $"Voucher type '{voucherType.Name}' is inactive and cannot take new vouchers.");
        }

        return voucherType;
    }

    private async Task<(Voucher Voucher, VoucherRevision Revision)> RequireCurrentRevisionAsync(
        Guid voucherId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException("VoucherId must not be empty.", nameof(voucherId));
        }

        var voucher = await _unitOfWork.Repository<Voucher>()
            .GetByIdAsync(voucherId, cancellationToken)
            .ConfigureAwait(false);

        if (voucher is null || voucher.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Voucher '{voucherId}' does not exist.");
        }

        var revision = await _unitOfWork.Repository<VoucherRevision>()
            .SingleOrDefaultAsync(new CurrentRevisionOf(voucherId), cancellationToken)
            .ConfigureAwait(false);

        if (revision is null)
        {
            throw new InvalidOperationException(
                $"Voucher '{voucherId}' has no current revision, which should be impossible outside " +
                "an open alteration transaction. The books may be corrupt — investigate before posting.");
        }

        return (voucher, revision);
    }

    private static PostedVoucherSummary Summarise(VoucherRevision revision, VoucherType voucherType)
    {
        return new PostedVoucherSummary(
            revision.VoucherId,
            revision.Id,
            revision.VoucherTypeId,
            revision.RevisionNumber,
            revision.Number,
            revision.VoucherDate,
            revision.Status,
            voucherType.Name,
            voucherType.Kind,
            revision.TotalDebit,
            revision.TotalCredit,
            revision.Narration,
            revision.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new PostedVoucherLine(
                    line.LedgerId,
                    line.DebitAmount,
                    line.CreditAmount,
                    line.Narration))
                .ToList());
    }

    private sealed record ResolvedLine(
        int LineNumber,
        Ledger Ledger,
        decimal Amount,
        string? Narration,
        Guid? CostCentreId);

    private sealed record ResolvedContent(IReadOnlyList<ResolvedLine> Lines);

    private sealed class ActiveVoucherTypes : Specification<VoucherType>
    {
        public ActiveVoucherTypes(Guid companyId)
        {
            Where(type => type.CompanyId == companyId && type.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class LedgersByIds : Specification<Ledger>
    {
        public LedgersByIds(IEnumerable<Guid> ids)
        {
            Where(ledger => ids.Contains(ledger.Id));
            ApplyNoTracking();
        }
    }

    private sealed class GroupsOfCompany : Specification<AccountGroup>
    {
        public GroupsOfCompany(Guid companyId)
        {
            Where(group => group.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class PeriodLockOf : Specification<PeriodLock>
    {
        public PeriodLockOf(Guid companyId)
        {
            Where(lockRow => lockRow.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class ClosedYearContaining : Specification<FinancialYear>
    {
        public ClosedYearContaining(Guid companyId, DateOnly date)
        {
            Where(year => year.CompanyId == companyId
                && year.StartDate <= date
                && year.EndDate >= date
                && year.IsClosed);
            ApplyNoTracking();
        }
    }

    private sealed class CurrentRevisionOf : Specification<VoucherRevision>
    {
        public CurrentRevisionOf(Guid voucherId)
        {
            Where(revision => revision.VoucherId == voucherId
                && revision.SupersededByRevisionId == null);

            // Totals and summaries are computed from the lines in memory; without this, a summary
            // would silently read zero rather than fail loudly.
            AddInclude(revision => revision.Lines);
        }
    }
}
