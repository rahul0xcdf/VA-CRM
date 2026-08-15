using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="VoucherType"/> (spec §5, §7.1).
/// </summary>
/// <remarks>
/// The type is a data row and the kind is an enum, so the owner can run "Cash Payment" and "Bank
/// Payment" as separate types with separate numbering while both behave as
/// <see cref="VoucherKind.Payment"/>.
/// </remarks>
internal sealed class VoucherTypeConfiguration : IEntityTypeConfiguration<VoucherType>
{
    public void Configure(EntityTypeBuilder<VoucherType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("VoucherTypes");

        builder.HasKey(type => type.Id);

        builder.Property(type => type.Id).ValueGeneratedNever();

        builder.Property(type => type.CompanyId).IsRequired();

        builder.Property(type => type.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(type => type.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        // By name, never by integer: Phase 5 and Phase 8 reporting SQL filters on this value, and a
        // later reordering of VoucherKind must not reclassify existing vouchers (spec §5).
        builder.Property(type => type.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(type => type.IsSystem).IsRequired();

        // Deliberately NOT a foreign key — the one exception to the rule applied everywhere else in
        // this phase (spec §5). VoucherType → VoucherNumberSeries → VoucherType is a cycle, and with
        // an FK in place EF must break it by inserting the type with a null DefaultSeriesId and then
        // issuing an UPDATE. That UPDATE is a real change through SaveChangesAsync, so it would
        // write a spurious edit-log row on every single company provisioning — noise in exactly the
        // log an auditor reads first.
        builder.Property(type => type.DefaultSeriesId);

        builder.Property(type => type.IsActive).IsRequired();

        builder.Property(type => type.CreatedAtUtc).IsRequired();
        builder.Property(type => type.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(type => type.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(type => new { type.CompanyId, type.NameNormalised }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(type => type.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
