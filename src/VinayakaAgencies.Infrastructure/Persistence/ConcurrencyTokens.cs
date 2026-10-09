using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// Marks the optimistic-concurrency token on every mutable entity, and refreshes it on every write.
/// </summary>
/// <remarks>
/// <b>Why a domain-managed <see cref="Guid"/> and not <c>rowversion</c>.</b> SQL Server's
/// <c>rowversion</c> is the obvious choice and is unavailable: SQLite has no equivalent, and
/// `phase-9-multiuser.md` C9.4 requires the Phase 1–8 suites to pass <i>unchanged</i> on both
/// providers. A token the domain maintains behaves identically on each, so "unchanged" stays
/// honest rather than meaning "unchanged except for the concurrency tests".
/// <para>
/// <b>Why <see cref="IConcurrencyStamped"/> and not <c>Entity</c>.</b> Declaring the token for that
/// interface draws the line exactly where the model draws it — <see cref="IAuditStamped"/> extends it,
/// and <c>VoucherNumberSequence</c> implements it directly. On <c>Entity</c> the token would also land
/// on the append-only types — <c>VoucherRevision</c>, <c>JournalLine</c>, <c>StockLedgerEntry</c> —
/// where it is dead weight and, worse, implies those rows can be updated, which ADR-0002 forbids.
/// </para>
/// <para>
/// <b>Why the token is a real property.</b> It was a shadow property first, and that broke every
/// service which reads with <c>AsNoTracking</c> and then calls <c>Update</c> on the detached
/// instance: a no-tracking read carries no shadow values, so re-attaching sent an all-zero original
/// token, matched no row, and turned an ordinary save into a spurious lost-update.
/// <c>GstRateScheduleTests</c> is what caught it. See <see cref="IConcurrencyStamped"/>.
/// </para>
/// <para>
/// <b>Existing rows.</b> The migration adds the column with an all-zero default, so every row
/// already in a Phase 1–8 database starts on the same token. Detection still works from the first
/// write: the update matches on the value that was loaded, so whichever client saves first replaces
/// it and the second client's <c>WHERE</c> finds nothing.
/// </para>
/// </remarks>
internal static class ConcurrencyTokens
{
    /// <summary>
    /// Marks the token as the concurrency check on every <see cref="IConcurrencyStamped"/> entity.
    /// </summary>
    /// <remarks>
    /// Must run after the per-entity configurations have been applied, or the model will not yet
    /// know about the entity types this walks.
    /// </remarks>
    internal static void Declare(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.ClrType is null || !typeof(IConcurrencyStamped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(IConcurrencyStamped.ConcurrencyToken))
                .IsConcurrencyToken()
                .IsRequired();
        }
    }

    /// <summary>
    /// Gives every inserted and updated row a fresh token.
    /// </summary>
    /// <remarks>
    /// Assigning the <i>current</i> value on an update is what makes this work: EF Core puts the
    /// <i>original</i> value into the <c>WHERE</c> clause and writes the new one, so a row somebody
    /// else has already changed matches nothing and the save reports zero rows affected.
    /// </remarks>
    internal static void Stamp(ChangeTracker changeTracker)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);

        foreach (var entry in changeTracker.Entries<IConcurrencyStamped>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            entry.Entity.ConcurrencyToken = Guid.NewGuid();
        }
    }
}
