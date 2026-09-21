using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PayHead"/> (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// The index on <c>(CompanyId, Statute)</c> is what lets the payroll engine find "the heads this
/// statute produces" without matching on a name. That is the difference between a flag switching a
/// scheme off and a <c>switch</c> arm knowing which head is the PF one (checkpoint C7.7).
/// </remarks>
internal sealed class PayHeadConfiguration : IEntityTypeConfiguration<PayHead>
{
    public void Configure(EntityTypeBuilder<PayHead> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayHeads");

        builder.HasKey(head => head.Id);
        builder.Property(head => head.Id).ValueGeneratedNever();

        builder.Property(head => head.CompanyId).IsRequired();
        builder.Property(head => head.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(head => head.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(head => head.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(head => head.Basis)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(head => head.Statute)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(head => head.LedgerId).IsRequired();
        builder.Property(head => head.SortOrder).IsRequired();
        builder.Property(head => head.IsActive).IsRequired();

        builder.Property(head => head.CreatedAtUtc).IsRequired();
        builder.Property(head => head.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(head => head.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // A computed projection of Basis, never a column.
        builder.Ignore(head => head.IsStatutory);

        builder.HasIndex(head => new { head.CompanyId, head.NameNormalised })
            .IsUnique()
            .HasFilter(IndexFilters.IsTrue(nameof(PayHead.IsActive)));

        builder.HasIndex(head => new { head.CompanyId, head.Statute });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(head => head.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(head => head.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


