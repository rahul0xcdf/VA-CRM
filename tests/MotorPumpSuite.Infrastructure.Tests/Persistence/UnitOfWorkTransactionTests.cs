using Microsoft.Extensions.DependencyInjection;
using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Entities;
using MotorPumpSuite.Infrastructure.Tests.TestSupport;

namespace MotorPumpSuite.Infrastructure.Tests.Persistence;

/// <summary>
/// The transaction boundary Phase 3's atomic invoice posting depends on.
/// </summary>
/// <remarks>
/// Checkpoint C3.3 — a tax invoice that fails midway leaves neither a stock nor a ledger row — is
/// met by this behaviour or it is not met at all, so it is proven here against a real database
/// rather than assumed.
/// </remarks>
public sealed class UnitOfWorkTransactionTests
{
    [Fact]
    public async Task Transaction_Committed_PersistsEverySave()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync();

            await unitOfWork.Repository<Company>().AddAsync(new Company("One", "One Ltd", "29"));
            await unitOfWork.SaveChangesAsync();

            await unitOfWork.Repository<Company>().AddAsync(new Company("Two", "Two Ltd", "29"));
            await unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();
        });

        var count = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .CountAsync());

        count.ShouldBe(2);
    }

    [Fact]
    public async Task Transaction_RolledBack_DiscardsSavedChanges()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync();

            await unitOfWork.Repository<Company>()
                .AddAsync(new Company("Rolled Back", "Rolled Back Ltd", "29"));
            await unitOfWork.SaveChangesAsync();

            await transaction.RollbackAsync();
        });

        var count = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .CountAsync());

        count.ShouldBe(0);
    }

    [Fact]
    public async Task Transaction_DisposedWithoutCommit_RollsBack()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            // No CommitAsync: leaving the using block must abandon the work rather than keep it.
            // Relying on a caller to remember an explicit rollback in a catch block is how
            // half-posted invoices happen.
            await using var transaction = await unitOfWork.BeginTransactionAsync();

            await unitOfWork.Repository<Company>()
                .AddAsync(new Company("Abandoned", "Abandoned Ltd", "29"));
            await unitOfWork.SaveChangesAsync();
        });

        var count = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .CountAsync());

        count.ShouldBe(0);
    }

    [Fact]
    public async Task Transaction_WhenExceptionEscapes_LeavesNoPartialWrite()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(async sp =>
            {
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                await using var transaction = await unitOfWork.BeginTransactionAsync();

                await unitOfWork.Repository<Company>()
                    .AddAsync(new Company("First Leg", "First Leg Ltd", "29"));
                await unitOfWork.SaveChangesAsync();

                throw new InvalidOperationException("Second leg failed.");
            }));

        var count = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .CountAsync());

        count.ShouldBe(0);
    }

    [Fact]
    public async Task Repository_CalledTwiceForTheSameType_SharesOneChangeTracker()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var first = unitOfWork.Repository<Company>();
            var second = unitOfWork.Repository<Company>();

            // Two independent views of the same pending work would let one commit silently drop
            // what the other had staged.
            first.ShouldBeSameAs(second);

            await first.AddAsync(new Company("Shared", "Shared Ltd", "29"));
            await unitOfWork.SaveChangesAsync();

            (await second.CountAsync()).ShouldBe(1);
        });
    }
}
