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

    /// <summary>
    /// The migrations this database is missing, in order, without applying any of them.
    /// </summary>
    /// <remarks>
    /// What the startup gate asks. From Phase 9 a stale schema is not something to fix quietly
    /// while three clients are launching — the host has to be able to see that an upgrade is due,
    /// name it, and stop.
    /// </remarks>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies every pending migration, regardless of
    /// <see cref="Configuration.DatabaseOptions.ApplyMigrationsOnStartup"/>.
    /// </summary>
    /// <remarks>
    /// The explicit, gated, single-operator step `phase-9-multiuser.md` §2 asks for. It ignores the
    /// startup switch deliberately: the switch answers "may this happen unattended?", and the
    /// answer being no is the reason somebody is invoking this by hand.
    /// <para>
    /// Being single-operator is enforced by the human procedure, not by the database: everybody
    /// else is out of the product while this runs. A server-side application lock would make that
    /// a guarantee rather than an instruction, and is noted in §4b as the SQL Server refinement —
    /// it cannot be written against a provider this machine has no instance of.
    /// </para>
    /// </remarks>
    /// <returns>The migrations applied, in order. Empty when the schema was already current.</returns>
    Task<IReadOnlyList<string>> ApplyPendingMigrationsAsync(CancellationToken cancellationToken = default);
}
