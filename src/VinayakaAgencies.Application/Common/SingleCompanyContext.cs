using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Application.Common;

/// <summary>
/// Resolves the one company this install keeps books for, and caches it for the scope.
/// </summary>
/// <remarks>
/// Phases 1–8 are single-company and single-user, so "which company" has exactly one answer and
/// finding it is a query, not a choice. The interface exists anyway (spec §10) so that adding the
/// second company at Phase 9 is a different <see cref="ICompanyContext"/> plus a picker, not a
/// migration and a signature change on every service.
/// <para>
/// It deliberately does <b>not</b> invent a company when the database has none. A company carries a
/// legal name, a state code and eventually a GSTIN, all of which end up on filed returns; a
/// placeholder row would be indistinguishable from a real one by the time anybody noticed, and the
/// state code in particular decides CGST/SGST versus IGST from Phase 5 onward. Provisioning is a
/// deliberate act with real inputs, so a missing company is an error with an instruction attached
/// rather than a default quietly filled in.
/// </para>
/// </remarks>
public sealed class SingleCompanyContext : ICompanyContext
{
    private readonly IUnitOfWork _unitOfWork;

    private Guid? _companyId;

    public SingleCompanyContext(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Guid CompanyId => _companyId ?? throw new InvalidOperationException(
        $"No company has been resolved yet. Call {nameof(EnsureCompanyAsync)} before reading " +
        $"{nameof(CompanyId)} — a service that posts against an unresolved company would write " +
        "rows with an empty CompanyId, which the foreign key rejects and no report would find.");

    /// <inheritdoc />
    public bool HasCompany => _companyId.HasValue;

    /// <inheritdoc />
    public async Task<Guid> EnsureCompanyAsync(CancellationToken cancellationToken = default)
    {
        if (_companyId.HasValue)
        {
            return _companyId.Value;
        }

        var companies = await _unitOfWork.Repository<Company>()
            .ListAsync(new ActiveCompanies(), cancellationToken)
            .ConfigureAwait(false);

        if (companies.Count == 0)
        {
            throw new InvalidOperationException(
                "This install has no active company, so there are no books to post to. Create one " +
                "and provision its chart of accounts (ICompanyProvisioningService) before any " +
                "accounting operation. A company is not defaulted into existence on purpose: its " +
                "legal name and state code appear on filed returns.");
        }

        // Oldest first, so the answer is the same on every call and on every machine even after a
        // second company appears — which it may, since nothing prevents it, and a context that
        // silently changed which books it meant would be far worse than one that is merely
        // arbitrary. Choosing between several is Phase 9's problem, together with the picker.
        //
        // Ordered here rather than in the specification because SQLite cannot ORDER BY a
        // DateTimeOffset: EF Core refuses to translate it, since a stored offset makes lexical order
        // and chronological order two different things. The set is one row in this phase and a
        // handful at worst, so sorting it in memory costs nothing and is honest about where the
        // ordering happens.
        _companyId = companies.OrderBy(company => company.CreatedAtUtc).First().Id;

        return _companyId.Value;
    }

    private sealed class ActiveCompanies : Specification<Company>
    {
        internal ActiveCompanies()
            : base(company => company.IsActive)
        {
            ApplyNoTracking();
        }
    }
}
