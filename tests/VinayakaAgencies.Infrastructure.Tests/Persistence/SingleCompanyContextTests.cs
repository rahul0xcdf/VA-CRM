using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// The active-company plumbing spec §10 requires, exercised against a real database.
/// </summary>
public sealed class SingleCompanyContextTests
{
    [Fact]
    public async Task EnsureCompanyAsync_WithOneCompany_ResolvesIt()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var created = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyService>().CreateAsync(
                new CreateCompanyRequest("Vinayaka Agencies", "Vinayaka Agencies", "29")));

        var resolved = await host.InScopeAsync(async sp =>
        {
            var context = sp.GetRequiredService<ICompanyContext>();

            context.HasCompany.ShouldBeFalse();

            var id = await context.EnsureCompanyAsync();

            context.HasCompany.ShouldBeTrue();
            context.CompanyId.ShouldBe(id);

            return id;
        });

        resolved.ShouldBe(created.Id);
    }

    [Fact]
    public async Task EnsureCompanyAsync_WithNoCompany_ThrowsRatherThanInventingOne()
    {
        // A defaulted company would carry a placeholder legal name and state code onto filed
        // returns, and would be indistinguishable from a real row by the time anyone noticed.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
            await Should.ThrowAsync<InvalidOperationException>(async () =>
                await sp.GetRequiredService<ICompanyContext>().EnsureCompanyAsync()));
    }

    [Fact]
    public async Task CompanyId_BeforeEnsureCompanyAsync_Throws()
    {
        // Reading it early would hand out Guid.Empty, which the CompanyId foreign key rejects on
        // save — but only after the caller has built a whole voucher against it.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(sp =>
        {
            var context = sp.GetRequiredService<ICompanyContext>();

            Should.Throw<InvalidOperationException>(() => context.CompanyId);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EnsureCompanyAsync_WithSeveralCompanies_ResolvesTheOldestOnEveryCall()
    {
        // Nothing prevents a second company row, and a context that silently changed which books it
        // meant would be worse than one that is merely arbitrary. Choosing between them is Phase 9's
        // problem, together with the picker.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var first = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyService>().CreateAsync(
                new CreateCompanyRequest("First Books", "First Books", "29")));

        host.Clock.Advance(TimeSpan.FromMinutes(5));

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyService>().CreateAsync(
                new CreateCompanyRequest("Second Books", "Second Books", "29")));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var resolved = await host.InScopeAsync(sp =>
                sp.GetRequiredService<ICompanyContext>().EnsureCompanyAsync());

            resolved.ShouldBe(first.Id);
        }
    }
}
