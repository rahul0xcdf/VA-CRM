using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Claims voucher numbers from a type's series, inside the caller's transaction (spec §7.2).
/// </summary>
/// <remarks>
/// Allocation happens at post time and only after validation, so a rejected voucher never consumes
/// a number. Numbers are claimed from the series' <see cref="VoucherNumberSequence"/> high-water
/// mark — one row per (series, financial year) pair, which is how the reset-each-year rule of
/// decision D2 is realised: there is no reset job to run at the wrong moment.
/// <para>
/// A manual override raises the mark past itself so later automatic numbers continue after it and
/// cannot collide; the override itself stays visible on the revision (<c>IsManualNumber</c>) and in
/// the edit log. Reuse of an existing current-revision number is refused — the filtered unique
/// index would refuse it anyway, but an explicit check gives an error a person can act on.
/// </para>
/// </remarks>
public sealed class VoucherNumberAllocator(
    IUnitOfWork unitOfWork,
    IFinancialYearProvider financialYearProvider) : IVoucherNumberAllocator
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly IFinancialYearProvider _financialYearProvider = financialYearProvider
        ?? throw new ArgumentNullException(nameof(financialYearProvider));

    /// <inheritdoc />
    public async Task<AllocatedVoucherNumber> AllocateAsync(
        Guid companyId,
        Guid voucherTypeId,
        DateOnly voucherDate,
        string? manualNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (voucherTypeId == Guid.Empty)
        {
            throw new ArgumentException(
                "A number can only be allocated for a specific voucher type.", nameof(voucherTypeId));
        }

        var voucherType = await _unitOfWork.Repository<VoucherType>()
            .GetByIdAsync(voucherTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (voucherType is null || voucherType.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                $"Voucher type '{voucherTypeId}' does not exist for this company.");
        }

        var series = await ResolveSeriesAsync(voucherType, cancellationToken).ConfigureAwait(false);
        var financialYear = await _financialYearProvider
            .GetOrCreateAsync(companyId, voucherDate, cancellationToken)
            .ConfigureAwait(false);

        var sequence = await ResolveSequenceAsync(series, financialYear, cancellationToken)
            .ConfigureAwait(false);

        if (manualNumber is not null)
        {
            return await AllocateManualAsync(
                series,
                financialYear,
                sequence,
                manualNumber,
                cancellationToken)
                .ConfigureAwait(false);
        }

        var next = sequence.Allocate(series.StartNumber);

        return new AllocatedVoucherNumber(
            series.Id,
            financialYear.Id,
            series.Format(next),
            next,
            IsManual: false);
    }

    private async Task<VoucherNumberSeries> ResolveSeriesAsync(
        VoucherType voucherType,
        CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<VoucherNumberSeries>();

        if (voucherType.DefaultSeriesId is { } defaultSeriesId)
        {
            var series = await repository.GetByIdAsync(defaultSeriesId, cancellationToken)
                .ConfigureAwait(false);

            if (series is null || !series.IsActive)
            {
                throw new InvalidOperationException(
                    $"Voucher type '{voucherType.Name}' names a default numbering series that is " +
                    "missing or inactive, so no number can be allocated.");
            }

            return series;
        }

        var active = await repository
            .ListAsync(new ActiveSeriesOfType(voucherType.CompanyId, voucherType.Id), cancellationToken)
            .ConfigureAwait(false);

        return active.Count switch
        {
            1 => active[0],
            0 => throw new InvalidOperationException(
                $"Voucher type '{voucherType.Name}' has no active numbering series. Add one before posting."),
            _ => throw new InvalidOperationException(
                $"Voucher type '{voucherType.Name}' has {active.Count} active numbering series and no " +
                "default. Set DefaultSeriesId so allocation is deterministic."),
        };
    }

    private async Task<VoucherNumberSequence> ResolveSequenceAsync(
        VoucherNumberSeries series,
        FinancialYear financialYear,
        CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<VoucherNumberSequence>();

        var existing = await repository
            .SingleOrDefaultAsync(
                new SequenceOfSeriesAndYear(series.Id, financialYear.Id),
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var created = new VoucherNumberSequence(series.Id, financialYear.Id);
        await repository.AddAsync(created, cancellationToken).ConfigureAwait(false);

        return created;
    }

    private async Task<AllocatedVoucherNumber> AllocateManualAsync(
        VoucherNumberSeries series,
        FinancialYear financialYear,
        VoucherNumberSequence sequence,
        string manualNumber,
        CancellationToken cancellationToken)
    {
        if (!series.AllowManualOverride)
        {
            throw new InvalidOperationException(
                $"Series '{series.Name}' does not allow manual number overrides.");
        }

        var supplied = manualNumber.Trim();

        if (!VoucherNumberParser.TryParse(series, supplied, out var numeric))
        {
            throw new InvalidOperationException(
                $"'{supplied}' is not a number of series '{series.Name}', whose numbers look like " +
                $"'{series.Format(series.StartNumber)}'.");
        }

        if (numeric < 1)
        {
            throw new InvalidOperationException("A voucher number must be at least 1.");
        }

        var taken = await _unitOfWork.Repository<VoucherRevision>()
            .AnyAsync(
                new CurrentRevisionWithNumber(series.Id, financialYear.Id, VoucherRevision.Normalise(supplied)),
                cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            throw new InvalidOperationException(
                $"'{supplied}' is already held by another voucher in series '{series.Name}' for " +
                $"financial year {financialYear.Label}. Numbers are never reused.");
        }

        sequence.ObserveManualNumber(numeric);

        return new AllocatedVoucherNumber(
            series.Id,
            financialYear.Id,
            supplied,
            numeric,
            IsManual: true);
    }

    private sealed class ActiveSeriesOfType : Specification<VoucherNumberSeries>
    {
        public ActiveSeriesOfType(Guid companyId, Guid voucherTypeId)
        {
            Where(series => series.CompanyId == companyId
                && series.VoucherTypeId == voucherTypeId
                && series.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class SequenceOfSeriesAndYear : Specification<VoucherNumberSequence>
    {
        public SequenceOfSeriesAndYear(Guid seriesId, Guid financialYearId)
        {
            Where(sequence => sequence.SeriesId == seriesId
                && sequence.FinancialYearId == financialYearId);
        }
    }

    private sealed class CurrentRevisionWithNumber : Specification<VoucherRevision>
    {
        public CurrentRevisionWithNumber(Guid seriesId, Guid financialYearId, string numberNormalised)
        {
            Where(revision => revision.SeriesId == seriesId
                && revision.FinancialYearId == financialYearId
                && revision.NumberNormalised == numberNormalised
                && revision.SupersededByRevisionId == null);
            ApplyNoTracking();
        }
    }
}
