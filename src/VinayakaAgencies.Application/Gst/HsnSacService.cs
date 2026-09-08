using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Gst;

namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Maintains the HSN/SAC master through the unit of work (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Uniqueness among active rows is checked here and backed by a filtered unique index, so two
/// active rows for one code cannot exist even under a race. Deactivation is the only retirement
/// path: rate schedules and posted invoices keep pointing at their row forever.
/// </remarks>
public sealed class HsnSacService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IHsnSacService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<HsnSacSummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<HsnSac>()
                .ListAsync(new CodesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(code => includeInactive || code.IsActive)
            .OrderBy(code => code.Code, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<HsnSacSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var code = await RequireCodeAsync(companyId, id, cancellationToken).ConfigureAwait(false);

        return ToSummary(code);
    }

    /// <inheritdoc />
    public async Task<HsnSacSummary> SaveAsync(
        SaveHsnSacRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        if (request.Id.HasValue)
        {
            var existing = await RequireCodeAsync(companyId, request.Id.Value, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(existing.Code, NormalisedCode(request.Code), StringComparison.Ordinal))
            {
                await RequireCodeFreeAsync(companyId, request.Code, excludeId: existing.Id, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetCode(existing, request.Code);
            existing.SetDescription(request.Description);

            if (request.IsActive)
            {
                existing.Activate();
            }
            else
            {
                existing.Deactivate();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ToSummary(existing);
        }

        await RequireCodeFreeAsync(companyId, request.Code, excludeId: null, cancellationToken)
            .ConfigureAwait(false);

        var created = new HsnSac(companyId, request.Code, request.Description);

        await _unitOfWork.Repository<HsnSac>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(created);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireCodeAsync(companyId, id, cancellationToken).ConfigureAwait(false)).Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireCodeAsync(companyId, id, cancellationToken).ConfigureAwait(false)).Activate();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalisedCode(string code) => code.Trim().ToUpperInvariant();

    private static void SetCode(HsnSac code, string value)
    {
        // The constructor normalises; a revision re-runs the same guards by rebuilding nothing —
        // HSN codes are immutable identity, so only description and activity may change. A code
        // correction is retire-and-recreate, which keeps rate history honest.
        if (!string.Equals(code.Code, NormalisedCode(value), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An HSN/SAC code cannot be renamed once created; deactivate it and create the " +
                "corrected code so its rate history stays attached to the right classification.");
        }
    }

    private async Task<HsnSac> RequireCodeAsync(
        Guid companyId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var code = await _unitOfWork.Repository<HsnSac>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return code is null || code.CompanyId != companyId
            ? throw new InvalidOperationException("This HSN/SAC code does not exist in this company's books.")
            : code;
    }

    private async Task RequireCodeFreeAsync(
        Guid companyId,
        string code,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var normalised = NormalisedCode(code);

        var clash = (await _unitOfWork.Repository<HsnSac>()
                .ListAsync(new ActiveCodeOfCompany(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault(candidate => !excludeId.HasValue || candidate.Id != excludeId.Value);

        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"An active HSN/SAC code '{normalised}' already exists; codes are unique among " +
                "a company's active rows.");
        }
    }

    private static HsnSacSummary ToSummary(HsnSac code) =>
        new(code.Id, code.Code, code.Description, code.IsActive);

    private sealed class CodesOfCompany : Specification<HsnSac>
    {
        public CodesOfCompany(Guid companyId)
        {
            Where(code => code.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class ActiveCodeOfCompany : Specification<HsnSac>
    {
        public ActiveCodeOfCompany(Guid companyId, string code)
        {
            Where(row => row.CompanyId == companyId && row.Code == code && row.IsActive);
            ApplyNoTracking();
        }
    }
}
