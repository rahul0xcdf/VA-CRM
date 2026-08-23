using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="StockGroup"/> (phase-2-inventory.md §1).
/// </summary>
internal sealed class StockGroupConfiguration : IEntityTypeConfiguration<StockGroup>
{
    public void Configure(EntityTypeBuilder<StockGroup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StockGroups");

        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();

        builder.Property(group => group.CompanyId).IsRequired();

        builder.Property(group => group.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(group => group.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(group => group.Description).HasMaxLength(ColumnLengths.Narration);
        builder.Property(group => group.IsActive).IsRequired();

        builder.Property(group => group.CreatedAtUtc).IsRequired();
        builder.Property(group => group.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(group => group.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(group => new { group.CompanyId, group.NameNormalised }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(group => group.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
