using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using VinayakaAgencies.Application.DependencyInjection;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.DependencyInjection;

// Aliased because this assembly has its own Tests.Persistence namespace, which would otherwise
// win the lookup for an unqualified `Persistence.` prefix.
using IDatabaseInitialiser = VinayakaAgencies.Infrastructure.Persistence.IDatabaseInitialiser;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// A disposable service provider wired to a throwaway SQLite file on disk.
/// </summary>
/// <remarks>
/// A real file, not an in-memory provider. Checkpoint criteria C0.2 and C0.3 are about the actual
/// migration running against the actual database engine — an in-memory double would happily pass
/// while a genuinely broken migration shipped. Composition goes through the public
/// <c>AddInfrastructure</c> entry point for the same reason: it is the path the application uses,
/// and reaching past it into the internal types would test something no caller can reach
/// (ADR-0001).
/// </remarks>
public sealed class SqliteTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _databasePath;

    public SqliteTestHost(
        FixedClock? clock = null,
        StubCurrentUser? currentUser = null,
        IIrpClient? irpClient = null,
        ICredentialProtector? credentialProtector = null)
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            "VinayakaAgencies.Tests",
            $"{Guid.NewGuid():N}.db");

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        Clock = clock ?? new FixedClock();
        CurrentUser = currentUser ?? new StubCurrentUser();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = $"Data Source={_databasePath}",
                ["Database:ApplyMigrationsOnStartup"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Registered before AddApplication so that its TryAddSingleton calls defer to these,
        // giving the audit-stamp assertions a clock and a user they control.
        services.TryAddSingleton<IClock>(Clock);
        services.TryAddSingleton<ICurrentUser>(CurrentUser);

        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Registered *after* AddInfrastructure, unlike the clock and user above, because these two are
        // not TryAdd registrations there: the IRP client is wired through AddHttpClient and the DPAPI
        // protector through TryAddSingleton, so overriding them means being the later registration
        // rather than the earlier one. Phase 6 needs both replaceable — there are no NIC sandbox
        // credentials (open-questions.md #10), and DPAPI would tie the tests to one Windows user.
        if (irpClient is not null)
        {
            services.AddScoped(_ => irpClient);
        }

        if (credentialProtector is not null)
        {
            services.AddSingleton(_ => credentialProtector);
        }

        _provider = services.BuildServiceProvider();
    }

    public FixedClock Clock { get; }

    public StubCurrentUser CurrentUser { get; }

    public string DatabasePath => _databasePath;

    /// <summary>
    /// Runs <paramref name="action"/> in its own DI scope, and therefore its own
    /// <c>DbContext</c> and change tracker.
    /// </summary>
    /// <remarks>
    /// The round-trip checkpoint (C0.3) requires the read to happen in a *new* context. Reading
    /// back through the same change tracker would return the instance still held in memory and
    /// would pass even if nothing had reached the database.
    /// </remarks>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = _provider.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    /// <summary>Applies pending migrations, exactly as the application does at startup.</summary>
    public Task<IReadOnlyList<string>> MigrateAsync() =>
        InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .InitialiseAsync());

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        // SQLite pools connections, and a pooled handle keeps the file locked on Windows long
        // enough to fail the delete. Clearing the pool first keeps the temp directory from
        // filling up over repeated runs.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TryDelete(_databasePath);
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
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
            // A leaked handle should not fail an otherwise passing test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
