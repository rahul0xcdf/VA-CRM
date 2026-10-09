using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Data;

/// <summary>
/// Guards the seam that lets each reporting area contribute its own SQL without every area editing
/// one file.
/// </summary>
/// <remarks>
/// The composition is reflection-driven, so the failure mode it has to be protected against is a
/// source that silently stops being discovered — which would surface as a query key that throws in
/// the product and nowhere else.
/// </remarks>
public sealed class SqlStatementSourceTests
{
    [Fact]
    public async Task Dialect_ResolvesAKeyContributedByAStatementSource()
    {
        // Company.Count moved out of the dialect and into CompanySqliteStatements. If reflection
        // discovery breaks, this is the test that notices rather than the shell window.
        await using var host = new SqliteTestHost();

        var sql = await host.InScopeAsync(sp => Task.FromResult(
            sp.GetRequiredService<ISqlDialect>().Resolve(ReportQueries.CompanyCount)));

        sql.ShouldContain("Companies");
    }

    [Fact]
    public async Task Dialect_ForAnUnregisteredKey_CanResolveIsFalseAndResolveThrows()
    {
        await using var host = new SqliteTestHost();
        var unregistered = new SqlQueryKey("Nothing.Registers.This");

        await host.InScopeAsync(sp =>
        {
            var dialect = sp.GetRequiredService<ISqlDialect>();

            dialect.CanResolve(unregistered).ShouldBeFalse();
            Should.Throw<KeyNotFoundException>(() => dialect.Resolve(unregistered));

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Dialect_ResolvesEveryKeyTheAccountingReportsDeclare()
    {
        // C1.11's second half. AccountingQueryKeys.All exists for exactly this walk — its own
        // remark says so — but nothing walked it until now, so a key declared in the Application
        // layer and never given SQL would have thrown the first time an owner opened that report.
        // It is also the test that keeps the Phase 9 SQL Server dialect honest: a key only SQLite
        // implements must fail the build, not the migration.
        await using var host = new SqliteTestHost();

        var unresolved = await host.InScopeAsync(sp =>
        {
            var dialect = sp.GetRequiredService<ISqlDialect>();

            return Task.FromResult(AccountingQueryKeys.All
                .Where(key => !dialect.CanResolve(key))
                .Select(key => key.Name)
                .ToList());
        });

        unresolved.ShouldBeEmpty(
            "every key in AccountingQueryKeys.All must have SQL in every registered dialect; "
            + $"missing: {string.Join(", ", unresolved)}");

        AccountingQueryKeys.All.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Dialect_NameIsTheProviderItsSourcesAreWrittenFor()
    {
        await using var host = new SqliteTestHost();

        var name = await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<ISqlDialect>().Name));

        name.ShouldBe("Sqlite");
    }
}
