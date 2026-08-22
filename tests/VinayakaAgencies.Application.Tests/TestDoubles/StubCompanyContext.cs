using VinayakaAgencies.Application.Common;

namespace VinayakaAgencies.Application.Tests.TestDoubles;

/// <summary>
/// A company context that always answers with one fixed company id — the single-company install
/// Phases 1–8 run as, pinned in tests so every service resolves the same books.
/// </summary>
public sealed class StubCompanyContext : ICompanyContext
{
    public StubCompanyContext(Guid companyId) => CompanyId = companyId;

    /// <summary>A stable non-empty id, for tests that never look the company up by id.</summary>
    public StubCompanyContext()
        : this(Guid.NewGuid())
    {
    }

    public Guid CompanyId { get; }

    public bool HasCompany => true;

    public Task<Guid> EnsureCompanyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CompanyId);
}
