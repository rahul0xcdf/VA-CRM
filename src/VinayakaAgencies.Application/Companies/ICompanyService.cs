namespace VinayakaAgencies.Application.Companies;

/// <summary>
/// Company master operations. The presentation layer talks to this, never to a repository.
/// </summary>
public interface ICompanyService
{
    Task<IReadOnlyList<CompanySummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<CompanySummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<CompanySummary> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken = default);
}

/// <param name="Name">Trading name shown in the UI.</param>
/// <param name="LegalName">Registered legal name, printed on the tax invoice (GST Rule 46).</param>
/// <param name="StateCode">GST state code — drives the CGST/SGST vs IGST split from Phase 5.</param>
/// <param name="Gstin">GSTIN, if registration details are known yet.</param>
/// <param name="FinancialYearStartMonth">4 (April) in India.</param>
public sealed record CreateCompanyRequest(
    string Name,
    string LegalName,
    string StateCode,
    string? Gstin = null,
    int FinancialYearStartMonth = 4,
    string BaseCurrencyCode = "INR");
