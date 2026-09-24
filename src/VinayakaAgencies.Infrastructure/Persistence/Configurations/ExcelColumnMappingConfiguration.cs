using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ExcelColumnMapping"/> (phase-8-interoperability.md §2).
/// </summary>
/// <remarks>
/// Uniqueness is soft, as elsewhere: a filtered unique index over <c>(CompanyId, NameNormalised)</c>
/// among active rows only, so a mapping the owner retired can have its name reused while two live
/// mappings called "Supplier price list" remain impossible.
/// </remarks>
internal sealed class ExcelColumnMappingConfiguration : IEntityTypeConfiguration<ExcelColumnMapping>
{
    public void Configure(EntityTypeBuilder<ExcelColumnMapping> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ExcelColumnMappings");

        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.Id).ValueGeneratedNever();

        builder.Property(mapping => mapping.CompanyId).IsRequired();
        builder.Property(mapping => mapping.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(mapping => mapping.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        // By name, like every other enum in this schema (§5): a later reordering of the enum cannot
        // then silently change what an existing row means.
        builder.Property(mapping => mapping.Target)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(mapping => mapping.SheetName).HasMaxLength(ColumnLengths.Name);
        builder.Property(mapping => mapping.HeaderRowNumber).IsRequired();
        builder.Property(mapping => mapping.IsActive).IsRequired();

        builder.Property(mapping => mapping.CreatedAtUtc).IsRequired();
        builder.Property(mapping => mapping.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(mapping => mapping.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(mapping => new { mapping.CompanyId, mapping.NameNormalised })
            .IsUnique()
            .HasFilter(IndexFilters.IsTrue(nameof(ExcelColumnMapping.IsActive)));

        builder.HasIndex(mapping => new { mapping.CompanyId, mapping.Target });

        builder.HasMany(mapping => mapping.Columns)
            .WithOne()
            .HasForeignKey(column => column.MappingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(mapping => mapping.Columns).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(mapping => mapping.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
