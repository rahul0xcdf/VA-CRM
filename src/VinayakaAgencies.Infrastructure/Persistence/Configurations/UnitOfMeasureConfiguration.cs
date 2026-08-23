using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="UnitOfMeasure"/> (phase-2-inventory.md §2).
/// </summary>
internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UnitsOfMeasure");

        builder.HasKey(unit => unit.Id);
        builder.Property(unit => unit.Id).ValueGeneratedNever();

        builder.Property(unit => unit.CompanyId).IsRequired();

        builder.Property(unit => unit.Code).IsRequired().HasMaxLength(ColumnLengths.UnitCode);
        builder.Property(unit => unit.CodeNormalised).IsRequired().HasMaxLength(ColumnLengths.UnitCode);
        builder.Property(unit => unit.DecimalPlaces).IsRequired();
        builder.Property(unit => unit.Description).HasMaxLength(ColumnLengths.Narration);
        builder.Property(unit => unit.IsActive).IsRequired();

        builder.Property(unit => unit.CreatedAtUtc).IsRequired();
        builder.Property(unit => unit.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(unit => unit.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(unit => new { unit.CompanyId, unit.CodeNormalised }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(unit => unit.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
