using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application layer. Call <c>AddInfrastructure</c> as well — this layer declares
    /// what it needs (<see cref="IUnitOfWork"/>) but never says how it is satisfied.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ICurrentUser, EnvironmentCurrentUser>();

        services.AddScoped<ICompanyService, CompanyService>();

        return services;
    }
}
