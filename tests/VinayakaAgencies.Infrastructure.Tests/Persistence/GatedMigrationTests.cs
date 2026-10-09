using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Infrastructure.Configuration;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

// Aliased because this assembly has its own Tests.Persistence namespace, which would otherwise win
// the lookup for an unqualified `Persistence.` prefix.
using IDatabaseInitialiser = VinayakaAgencies.Infrastructure.Persistence.IDatabaseInitialiser;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// Migration-on-startup as the explicit, gated step `phase-9-multiuser.md` §2 requires.
/// </summary>
/// <remarks>
/// Auto-migrating at launch was fine while the product was single-user. With two or three clients
/// it is a corruption risk: they all start in the morning, and several of them can be inside
/// <c>Migrate</c> on the same database at once. The gate does not make that impossible — one
/// operator deciding when it happens does, and the gate is what lets them.
/// </remarks>
public sealed class GatedMigrationTests
{
    [Fact]
    public void ApplyMigrationsOnStartup_ByDefault_IsOff()
    {
        // The Phase 9 decision, pinned. It shipped as on for Phases 0–8, so this is the kind of
        // default that reverts in a merge and is noticed much later.
        new DatabaseOptions().ApplyMigrationsOnStartup.ShouldBeFalse();
    }

    [Fact]
    public async Task InitialiseAsync_StartupMigrationOff_AppliesNothingAndLeavesThemPending()
    {
        await using var host = new SqliteTestHost(applyMigrationsOnStartup: false);

        var applied = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .InitialiseAsync());

        var pending = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .GetPendingMigrationsAsync());

        applied.ShouldBeEmpty();
        pending.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task InitialiseAsync_StartupMigrationOn_StillMigrates()
    {
        // The single-user path is unchanged, which is what keeps the Phase 1–8 suites honest: they
        // each own a database file and switch this on deliberately.
        await using var host = new SqliteTestHost(applyMigrationsOnStartup: true);

        var applied = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .InitialiseAsync());

        var pending = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .GetPendingMigrationsAsync());

        applied.ShouldNotBeEmpty();
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyPendingMigrationsAsync_StartupMigrationOff_AppliesAnyway()
    {
        // It ignores the switch on purpose: the switch answers "may this happen unattended?", and
        // the answer being no is precisely why somebody is invoking this by hand.
        await using var host = new SqliteTestHost(applyMigrationsOnStartup: false);

        var applied = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .ApplyPendingMigrationsAsync());

        var pending = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .GetPendingMigrationsAsync());

        applied.ShouldNotBeEmpty();
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyPendingMigrationsAsync_AlreadyCurrent_AppliesNothing()
    {
        // The second operator to open the product after an upgrade must not re-run it.
        await using var host = new SqliteTestHost(applyMigrationsOnStartup: false);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .ApplyPendingMigrationsAsync());

        var second = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .ApplyPendingMigrationsAsync());

        second.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPendingMigrationsAsync_Always_NamesThemRatherThanCountingThem()
    {
        // The upgrade window lists these. "3 changes pending" is nothing an operator can act on or
        // report; a name is something they can read back over the phone.
        await using var host = new SqliteTestHost(applyMigrationsOnStartup: false);

        var pending = await host.InScopeAsync(sp => sp
            .GetRequiredService<IDatabaseInitialiser>()
            .GetPendingMigrationsAsync());

        pending.ShouldAllBe(name => !string.IsNullOrWhiteSpace(name));
        pending.ShouldContain(name => name.EndsWith("AddUsersRolesAndConcurrencyTokens", StringComparison.Ordinal));
    }
}
