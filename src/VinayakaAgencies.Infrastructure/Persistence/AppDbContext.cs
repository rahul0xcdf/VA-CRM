using Microsoft.EntityFrameworkCore;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// The EF Core context for a company database.
/// </summary>
/// <remarks>
/// Deliberately <c>internal</c>. ADR-0001 requires that no <c>DbContext</c> type leaves this
/// assembly — callers reach the database through <see cref="Domain.Abstractions.IUnitOfWork"/> and
/// <see cref="Domain.Abstractions.IRepository{TEntity}"/> only. That is what keeps the Phase 9
/// SQLite → SQL Server Express migration from becoming a rewrite, and it is why no
/// <c>DbSet&lt;T&gt;</c> properties are exposed here: entity access is via
/// <see cref="DbContext.Set{TEntity}"/> from the repository.
/// </remarks>
internal sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Money and quantity are decimal(18,4) everywhere (CLAUDE.md). Setting it as a convention
        // rather than per-property means a Phase 1 voucher amount cannot silently land on the
        // provider default and lose paise, and SQL Server will not fall back to decimal(18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);

        // Non-key strings default to a bounded width so nothing maps to nvarchar(max) by accident
        // in Phase 9. Configurations override this where a column genuinely needs more.
        configurationBuilder.Properties<string>().HaveMaxLength(512);
    }
}
