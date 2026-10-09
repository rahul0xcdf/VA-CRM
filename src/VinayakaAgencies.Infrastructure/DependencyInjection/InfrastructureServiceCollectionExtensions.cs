using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Infrastructure.Data;
using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Infrastructure.Einvoicing;
using VinayakaAgencies.Infrastructure.Interop;
using VinayakaAgencies.Infrastructure.Persistence;
using VinayakaAgencies.Infrastructure.Security;

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

        // Scoped, so one unit of work owns one context and therefore one change tracker.
        //
        // There is deliberately no audit interceptor registered here. The audit stamps and the
        // edit-log capture both live in AppDbContext.SaveChangesAsync, with IClock and ICurrentUser
        // as required constructor parameters, because spec §11.2 rules out an interceptor by name:
        // an interceptor can be dropped from this method by a future edit and nothing would fail —
        // the product would simply stop keeping an audit trail. A required constructor parameter
        // cannot be dropped without the build breaking.
        services.AddDbContext<AppDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

            builder.UseSqlite(
                SqliteConnectionStringResolver.Resolve(options),
                sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            // Diagnostics only, and nothing registers an IInterceptor in the product — so this is
            // a no-op at runtime. It exists because checkpoint C1.18 requires the emitted SQL of
            // an alteration to be observable through a DbCommandInterceptor, and the test must
            // watch *this* composition rather than a context assembled beside it. Note the comment
            // above: this is not a way to add audit behaviour, which stays in SaveChangesAsync
            // with required constructor parameters precisely so it cannot be unregistered.
            builder.AddInterceptors(provider.GetServices<Microsoft.EntityFrameworkCore
                .Diagnostics.IInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.TryAddSingleton<ISqlDialect, SqliteDialect>();
        services.TryAddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.AddScoped<ISqlQueryExecutor, DapperSqlQueryExecutor>();

        // Phase 9 §4a D2: application accounts need a one-way verifier. Stateless and thread-safe,
        // so a singleton. Deliberately registered beside ICredentialProtector and deliberately not
        // the same thing — that protects secrets we must replay to the IRP, this protects passwords
        // which must never be recoverable at all.
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddScoped<IDatabaseInitialiser, DatabaseInitialiser>();

        // Phase 6 e-invoicing: the two Application ports Infrastructure satisfies.
        //
        // The IRP client is registered through AddHttpClient so it gets a pooled, correctly
        // recycled handler and a real timeout — a HttpClient held in a field is the classic way to
        // end up with stale DNS, and a call that never times out defeats the retry path C6.5
        // depends on. It is typed rather than named so the port stays the only thing callers see.
        services.AddOptions<NicIrpOptions>()
            .Bind(configuration.GetSection(NicIrpOptions.SectionName));

        services.TryAddSingleton<ICredentialProtector, DpapiCredentialProtector>();

        // Singleton, because the typed client below is transient and would otherwise re-authenticate
        // on every call against a portal that rate-limits authentication.
        services.TryAddSingleton<IrpSessionCache>();

        services.AddHttpClient<IIrpClient, NicIrpClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<NicIrpOptions>>().Value;

            // Sandbox unless production has been configured and selected — §2 keeps production
            // behind the owner's sign-off, and an unset production address makes that explicit.
            var baseAddress = options.ProductionBaseAddress is { Length: > 0 } production
                ? production
                : options.SandboxBaseAddress;

            client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        // Phase 8 interoperability: the two workbook ports Infrastructure satisfies with ClosedXML
        // (MIT; never EPPlus — paid licence since v5). Singletons because neither holds state
        // between calls: each opens the file it is handed and closes it again.
        services.TryAddSingleton<IWorkbookReader, ClosedXmlWorkbookReader>();
        services.TryAddSingleton<IWorkbookWriter, ClosedXmlWorkbookWriter>();

        return services;
    }
}
