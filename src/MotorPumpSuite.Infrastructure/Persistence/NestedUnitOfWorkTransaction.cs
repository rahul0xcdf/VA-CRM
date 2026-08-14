using MotorPumpSuite.Domain.Abstractions;

namespace MotorPumpSuite.Infrastructure.Persistence;

/// <summary>
/// The handle returned when a transaction is requested inside one that is already open.
/// </summary>
/// <remarks>
/// Committing or rolling back an inner scope would decide the fate of work the outer scope still
/// owns, so this handle does nothing and lets the outermost scope remain in charge. Neither SQLite
/// nor SQL Server gives us true nested transactions, and silently promoting an inner commit to a
/// real one is the failure mode this type exists to prevent.
/// </remarks>
internal sealed class NestedUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    public static readonly NestedUnitOfWorkTransaction Instance = new();

    private NestedUnitOfWorkTransaction()
    {
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
