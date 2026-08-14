using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditStamped"/> entities with who changed them and when, on every save.
/// </summary>
/// <remarks>
/// This runs as an interceptor rather than as a helper a service is expected to call, because a
/// stamp that depends on being remembered is a stamp that will eventually be missed. Phase 1's
/// edit log (project.md §4.1, MCA Rule 3(1)) hangs off this same interception point for the same
/// reason: it must be impossible to bypass, including by an administrator.
/// <para>
/// These stamps are <b>not</b> the audit trail. They record only the latest writer; the full
/// before/after history arrives in Phase 1.
/// </para>
/// </remarks>
internal sealed class AuditStampInterceptor(IClock clock, ICurrentUser currentUser)
    : SaveChangesInterceptor
{
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        var user = _currentUser.UserName;

        // An unattributable change is not an audit trail (see ICurrentUser). Fail loudly rather
        // than writing a blank author into the books.
        if (string.IsNullOrWhiteSpace(user))
        {
            throw new InvalidOperationException(
                "ICurrentUser returned a blank user name. Every persisted change must be attributable.");
        }

        foreach (var entry in context.ChangeTracker.Entries<IAuditStamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy = user;

                    // Creation facts are immutable. Re-attaching a detached entity would otherwise
                    // let a stale or forged value overwrite the original author.
                    entry.Property(nameof(IAuditStamped.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(IAuditStamped.CreatedBy)).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }
}
