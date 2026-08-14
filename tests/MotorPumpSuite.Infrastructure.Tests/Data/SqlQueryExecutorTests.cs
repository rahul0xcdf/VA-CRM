using Microsoft.Extensions.DependencyInjection;
using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Entities;
using MotorPumpSuite.Infrastructure.Tests.TestSupport;

namespace MotorPumpSuite.Infrastructure.Tests.Data;

/// <summary>
/// The Dapper reporting seam.
/// </summary>
/// <remarks>
/// Phase 1 reports every balance through this path, so Phase 0 proves it reaches the same database
/// the repository writes to and that it agrees with the repository's own count. A reporting path
/// silently pointed at a different file is a bug that would not surface until the numbers
/// disagreed.
/// </remarks>
public sealed class SqlQueryExecutorTests
{
    [Fact]
    public async Task ExecuteScalarAsync_CompanyCount_AgreesWithTheRepository()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var repository = unitOfWork.Repository<Company>();

            await repository.AddAsync(new Company("Alpha Pumps", "Alpha Pumps Ltd", "29"));
            await repository.AddAsync(new Company("Beta Motors", "Beta Motors Ltd", "29"));
            await repository.AddAsync(new Company("Gamma Spares", "Gamma Spares Ltd", "29"));
            await unitOfWork.SaveChangesAsync();
        });

        var viaRepository = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .CountAsync());

        var viaDapper = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISqlQueryExecutor>()
            .ExecuteScalarAsync<long>(ReportQueries.CompanyCount));

        viaRepository.ShouldBe(3);
        viaDapper.ShouldBe(3);
    }

    [Fact]
    public async Task QueryAsync_CompanyCount_ReturnsARow()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var rows = await host.InScopeAsync(sp => sp
            .GetRequiredService<ISqlQueryExecutor>()
            .QueryAsync<long>(ReportQueries.CompanyCount));

        rows.Count.ShouldBe(1);
        rows[0].ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithAnUnregisteredKey_Throws()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        // A key no dialect implements is a startup-time bug, not a silently empty report.
        await Should.ThrowAsync<KeyNotFoundException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<ISqlQueryExecutor>()
                .ExecuteScalarAsync<long>(new SqlQueryKey("Nonexistent.Query"))));
    }
}
