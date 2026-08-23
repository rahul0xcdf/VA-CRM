namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// Brings the company database up to the current schema.
/// </summary>
/// <remarks>
/// Exposed as an interface so the WPF host can run migrations at startup without referencing
/// EF Core (ADR-0001) — the host knows only that the database must be made ready, not how.
/// </remarks>
public interface IDatabaseInitialiser
{
    /// <summary>
    /// Applies any pending migrations, honouring
    /// <see cref="Configuration.DatabaseOptions.ApplyMigrationsOnStartup"/>.
    /// </summary>
    /// <returns>The migrations applied by this call, in order. Empty when already up to date.</returns>
    Task<IReadOnlyList<string>> InitialiseAsync(CancellationToken cancellationToken = default);
}
