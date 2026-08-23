using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SalesSettings"/> (phase-4-customer-pricing.md §2).
/// </summary>
internal sealed class SalesSettingsConfiguration : IEntityTypeConfiguration<SalesSettings>
{
    public void Configure(EntityTypeBuilder<SalesSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SalesSettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.CompanyId).IsRequired();

        builder.Property(settings => settings.AllowPriceOverride).IsRequired();

        builder.Property(settings => settings.CreditLimitPolicy)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(settings => settings.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // One row per company — the same single-row discipline as InventorySettings.
        builder.HasIndex(settings => settings.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(settings => settings.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
