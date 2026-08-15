using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backs the shell window.
/// </summary>
/// <remarks>
/// Phase 0's shell has no features — it exists to prove the composition root works end to end.
/// It reads the company count twice on purpose: once through
/// <see cref="ICompanyService"/> (EF Core, via <c>IUnitOfWork</c>) and once through
/// <see cref="ISqlQueryExecutor"/> (Dapper, via a named <see cref="SqlQueryKey"/>). Both paths are
/// load-bearing from Phase 1 — writes through one, reports through the other — so both are
/// exercised now rather than discovered broken later. Checkpoint C0.7.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ICompanyService _companyService;
    private readonly ISqlQueryExecutor _queryExecutor;

    [ObservableProperty]
    private string _title = "Vinayaka Agencies";

    [ObservableProperty]
    private string _tagline = string.Empty;

    [ObservableProperty]
    private int _companyCountViaRepository;

    [ObservableProperty]
    private long _companyCountViaDapper;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public ShellViewModel(
        ICompanyService companyService,
        ISqlQueryExecutor queryExecutor,
        IOptions<BrandingOptions> branding)
    {
        ArgumentNullException.ThrowIfNull(branding);

        _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));

        Title = branding.Value.DisplayName;
        Tagline = branding.Value.Tagline;
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            CompanyCountViaRepository = await _companyService
                .CountAsync(cancellationToken)
                .ConfigureAwait(true);

            CompanyCountViaDapper = await _queryExecutor
                .ExecuteScalarAsync<long>(ReportQueries.CompanyCount, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            Status = CompanyCountViaRepository == CompanyCountViaDapper
                ? "Repository and reporting paths agree."
                : "Mismatch between the repository and reporting counts.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
