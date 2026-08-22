using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="InventorySettings"/> (phase-2-inventory.md §2).
/// </summary>
internal sealed class InventorySettingsConfiguration : IEntityTypeConfiguration<InventorySettings>
{
    public void Configure(EntityTypeBuilder<InventorySettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InventorySettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.CompanyId).IsRequired();

        builder.Property(settings => settings.DefaultValuationMethod)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(settings => settings.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(settings => settings.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(settings => settings.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
