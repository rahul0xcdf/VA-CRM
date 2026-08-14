using MotorPumpSuite.Domain.Abstractions;

namespace MotorPumpSuite.Application.Tests.TestDoubles;

/// <summary>
/// Records commit/rollback/dispose calls so a test can assert transaction discipline without a real
/// database transaction underneath it.
/// </summary>
public sealed class FakeUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    public bool Committed { get; private set; }

    public bool RolledBack { get; private set; }

    public bool Disposed { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RolledBack = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
