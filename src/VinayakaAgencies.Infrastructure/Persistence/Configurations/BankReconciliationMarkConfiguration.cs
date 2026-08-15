using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="BankReconciliationMark"/> (spec §5, §12).
/// </summary>
/// <remarks>
/// A separate table rather than a column on <see cref="JournalLine"/>, because journal lines are
/// insert-only (§8.2) and a mark is added, changed and removed weeks after the line was posted.
/// Reconciliation state is operational metadata, not financial content — so it may change, and each
/// change is edit-logged like any other.
/// <para>
/// The unique index on <c>JournalLineId</c> is the "cleared once" rule: one line cannot be marked as
/// having cleared the bank on two different dates.
/// </para>
/// </remarks>
internal sealed class BankReconciliationMarkConfiguration
    : IEntityTypeConfiguration<BankReconciliationMark>
{
    public void Configure(EntityTypeBuilder<BankReconciliationMark> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("BankReconciliationMarks");

        builder.HasKey(mark => mark.Id);

        builder.Property(mark => mark.Id).ValueGeneratedNever();

        builder.Property(mark => mark.CompanyId).IsRequired();
        builder.Property(mark => mark.JournalLineId).IsRequired();

        // The date the bank shows, which is often not the voucher date. DateOnly, per §12.
        builder.Property(mark => mark.BankDate).IsRequired();

        // ClearedAtUtc / ClearedBy in the §5 table are the IAuditStamped creation pair.
        builder.Property(mark => mark.CreatedAtUtc).IsRequired();
        builder.Property(mark => mark.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(mark => mark.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(mark => mark.JournalLineId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(mark => mark.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<JournalLine>()
            .WithMany()
            .HasForeignKey(mark => mark.JournalLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
