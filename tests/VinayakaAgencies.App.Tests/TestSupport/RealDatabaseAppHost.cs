using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.DependencyInjection;
using IDatabaseInitialiser = VinayakaAgencies.Infrastructure.Persistence.IDatabaseInitialiser;

namespace VinayakaAgencies.App.Tests.TestSupport;

/// <summary>
/// The application's real service composition over a throwaway SQLite file, with one provisioned
/// company — the state a screen meets on a fresh install.
/// </summary>
/// <remarks>
/// Deliberately the production entry points (<c>AddApplication</c> + <c>AddInfrastructure</c>) and
/// nothing stubbed. A screen sweep built on fakes proves the fakes are consistent; this proves the
/// dependency graph the shell actually resolves is complete, against a database that actually
/// exists. Two of the defects found in this repository were exactly that shape — a service wired
/// into nothing, and a screen whose list could never populate.
/// </remarks>
internal sealed class RealDatabaseAppHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _databasePath;

    private RealDatabaseAppHost(ServiceProvider provider, string databasePath)
    {
        _provider = provider;
        _databasePath = databasePath;
    }

    public IServiceProvider Services => _provider;

    public Guid CompanyId { get; private set; }

    /// <param name="provisionCompany">
    /// False for the tests that drive first-run itself, which needs an install with no books at all.
    /// </param>
    public static async Task<RealDatabaseAppHost> StartAsync(bool provisionCompany = true)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "VinayakaAgencies.AppTests",
            $"db-{Guid.NewGuid():N}",
            "company.db");

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = $"Data Source={databasePath}",
                ["Database:ApplyMigrationsOnStartup"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddInfrastructure(configuration);

        var host = new RealDatabaseAppHost(services.BuildServiceProvider(), databasePath);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .InitialiseAsync());

        if (provisionCompany)
        {
            await host.ProvisionCompanyAsync();
        }

        return host;
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    /// <summary>A company with every phase's seed applied, exactly as first-run does.</summary>
    private async Task ProvisionCompanyAsync()
    {
        CompanyId = await InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");

            company.SetGstin("29ABCDE1234F1Z5");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        // Every seed the first-run wizard runs. If one of these is ever added and not called from
        // startup — which is what happened to EnsureGstSeedAsync in Phase 5 — the screens that
        // depend on it break on a fresh install and nowhere else.
        await InScopeAsync(async sp =>
        {
            var provisioning = sp.GetRequiredService<ICompanyProvisioningService>();

            await provisioning.ProvisionAsync(CompanyId);
            await provisioning.EnsureInventorySeedAsync(CompanyId);
            await provisioning.EnsurePipelineSeedAsync(CompanyId);
            await provisioning.EnsureCustomerSeedAsync(CompanyId);
            await provisioning.EnsureGstSeedAsync(CompanyId);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TryDelete(_databasePath);
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");

        try
        {
            var directory = Path.GetDirectoryName(_databasePath)!;

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
