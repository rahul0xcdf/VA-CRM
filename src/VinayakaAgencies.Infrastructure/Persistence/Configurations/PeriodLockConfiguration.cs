using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PeriodLock"/> (spec §5, §12).
/// </summary>
/// <remarks>
/// Spec §5 states "one row per company" in prose but does not tabulate the constraint. It is a
/// constraint all the same, and it is enforced here as a unique index on <c>CompanyId</c>: a second
/// lock row would leave the posting validation asking which of two locks applies, and the answer
/// would depend on row order. Moving the lock is an edit-logged master change — re-opening a filed
/// period is precisely what an auditor needs to be able to see, so the lock is a control, not a
/// hiding place.
/// </remarks>
internal sealed class PeriodLockConfiguration : IEntityTypeConfiguration<PeriodLock>
{
    public void Configure(EntityTypeBuilder<PeriodLock> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PeriodLocks");

        builder.HasKey(periodLock => periodLock.Id);

        builder.Property(periodLock => periodLock.Id).ValueGeneratedNever();

        builder.Property(periodLock => periodLock.CompanyId).IsRequired();

        // Null means nothing is locked, so the column is nullable by design.
        builder.Property(periodLock => periodLock.LockedThroughDate);

        builder.Property(periodLock => periodLock.Reason).HasMaxLength(ColumnLengths.Narration);

        // SetBy / SetAtUtc in the §5 table are the IAuditStamped pair: a change to the lock is a
        // master change, so who moved it and when are the stamps the audit trail already records.
        builder.Property(periodLock => periodLock.CreatedAtUtc).IsRequired();
        builder.Property(periodLock => periodLock.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(periodLock => periodLock.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(periodLock => periodLock.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(periodLock => periodLock.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
