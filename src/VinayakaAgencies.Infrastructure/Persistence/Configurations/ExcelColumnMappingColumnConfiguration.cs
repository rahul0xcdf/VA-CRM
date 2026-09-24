using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ExcelColumnMappingColumn"/>.</summary>
/// <remarks>
/// The unique index is over the normalised header rather than the display one, for the same reason
/// every master name is: SQLite's default collation is case-sensitive and SQL Server's is not, so
/// indexing the display text would accept "Item Name" and "ITEM NAME" as two columns here and then
/// fail to create the index during the Phase 9 move, with the duplicates already in the data.
/// </remarks>
internal sealed class ExcelColumnMappingColumnConfiguration : IEntityTypeConfiguration<ExcelColumnMappingColumn>
{
    public void Configure(EntityTypeBuilder<ExcelColumnMappingColumn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ExcelColumnMappingColumns");

        builder.HasKey(column => column.Id);
        builder.Property(column => column.Id).ValueGeneratedNever();

        builder.Property(column => column.CompanyId).IsRequired();
        builder.Property(column => column.MappingId).IsRequired();
        builder.Property(column => column.SourceHeader).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(column => column.SourceHeaderNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        // A field name, not an enum: the valid set differs per target, and the catalogue that knows
        // it lives in the Application layer (see ExcelColumnMappingColumn).
        builder.Property(column => column.TargetField).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(column => column.CreatedAtUtc).IsRequired();
        builder.Property(column => column.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(column => new { column.MappingId, column.SourceHeaderNormalised }).IsUnique();
        builder.HasIndex(column => new { column.MappingId, column.TargetField }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(column => column.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
