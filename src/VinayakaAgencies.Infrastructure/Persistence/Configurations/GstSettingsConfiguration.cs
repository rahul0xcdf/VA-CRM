using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="GstSettings"/> (phase-5-gst-returns.md §2).
/// </summary>
internal sealed class GstSettingsConfiguration : IEntityTypeConfiguration<GstSettings>
{
    public void Configure(EntityTypeBuilder<GstSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GstSettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.CompanyId).IsRequired();

        builder.Property(settings => settings.RoundOffLedgerName)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(settings => settings.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // One settings row per company, both directions.
        builder.HasIndex(settings => settings.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(settings => settings.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
