using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;
using VinayakaAgencies.Infrastructure.Data;
using VinayakaAgencies.Infrastructure.Persistence;
using VinayakaAgencies.Infrastructure.Persistence.Interceptors;

namespace VinayakaAgencies.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the persistence layer.
/// </summary>
/// <remarks>
/// Only interfaces from <c>VinayakaAgencies.Domain.Abstractions</c> are exposed to the container.
/// <c>AppDbContext</c>, the repositories and the unit of work are all internal, so no consumer can
/// resolve a provider type even by accident — which is the mechanical half of ADR-0001's rule that
/// no <c>DbContext</c> leaves this assembly.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                o => string.Equals(o.Provider, DatabaseProviders.Sqlite, StringComparison.OrdinalIgnoreCase),
                $"Only the '{DatabaseProviders.Sqlite}' provider is supported before Phase 9.")
            .ValidateOnStart();

        services.AddSingleton<AuditStampInterceptor>();

        // Scoped, so one unit of work owns one context and therefore one change tracker.
        services.AddDbContext<AppDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

            builder.UseSqlite(
                SqliteConnectionStringResolver.Resolve(options),
                sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            builder.AddInterceptors(provider.GetRequiredService<AuditStampInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.TryAddSingleton<ISqlDialect, SqliteDialect>();
        services.TryAddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.AddScoped<ISqlQueryExecutor, DapperSqlQueryExecutor>();

        services.AddScoped<IDatabaseInitialiser, DatabaseInitialiser>();

        return services;
    }
}
