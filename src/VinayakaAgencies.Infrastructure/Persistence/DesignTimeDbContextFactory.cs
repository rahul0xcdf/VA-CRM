using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// Builds an <see cref="AppDbContext"/> for the <c>dotnet ef</c> tooling.
/// </summary>
/// <remarks>
/// Design-time only. The tooling cannot start the WPF host to obtain a configured context, so it
/// gets a throwaway connection string pointed at a scratch file — migrations are generated from
/// the model, never from whatever database happens to be on the developer's machine.
/// <para>
/// <see cref="AppDbContext"/> requires an <see cref="IClock"/> and an <see cref="ICurrentUser"/>
/// because it cannot audit without them (spec §11.2). The tooling has neither, and it never saves —
/// it reads the model and writes a migration file. The stubs below therefore <b>throw</b> rather
/// than return a plausible-looking default: if a design-time code path ever does reach a save, the
/// correct outcome is a loud failure, not an audit row attributed to nobody at the epoch.
/// </para>
/// </remarks>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                "Data Source=design-time.db",
                sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(builder.Options, new UnavailableClock(), new UnavailableCurrentUser());
    }

    private sealed class UnavailableClock : IClock
    {
        public DateTimeOffset UtcNow => throw Unavailable();

        public DateOnly Today => throw Unavailable();

        private static InvalidOperationException Unavailable() => new(
            "No clock is available at design time. The dotnet-ef tooling builds the model and "
            + "writes migrations; it must never persist a change, because a change it persisted "
            + "could not be attributed to a user or a time (spec §11.2).");
    }

    private sealed class UnavailableCurrentUser : ICurrentUser
    {
        public string UserName => throw new InvalidOperationException(
            "No current user is available at design time. The dotnet-ef tooling builds the model "
            + "and writes migrations; it must never persist a change, because an unattributable "
            + "change is not an audit trail (spec §11.2, §11.6).");
    }
}
