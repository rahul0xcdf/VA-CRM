using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MotorPumpSuite.Infrastructure.Persistence;

/// <summary>
/// Builds an <see cref="AppDbContext"/> for the <c>dotnet ef</c> tooling.
/// </summary>
/// <remarks>
/// Design-time only. The tooling cannot start the WPF host to obtain a configured context, so it
/// gets a throwaway connection string pointed at a scratch file — migrations are generated from
/// the model, never from whatever database happens to be on the developer's machine.
/// </remarks>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                "Data Source=design-time.db",
                sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(builder.Options);
    }
}
