using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="EinvoiceSettings"/> (phase-6-einvoice-eway.md §4a).
/// </summary>
/// <remarks>
/// The four credential columns are BLOBs holding DPAPI ciphertext, never text. That is deliberate:
/// a TEXT column invites someone to look at it, and a plaintext credential written into a text column
/// by a future careless edit would be much harder to notice than one that fails to be a byte array.
/// The threshold is an INTEGER of minor units like every other money column (ADR-0003).
/// </remarks>
internal sealed class EinvoiceSettingsConfiguration : IEntityTypeConfiguration<EinvoiceSettings>
{
    public void Configure(EntityTypeBuilder<EinvoiceSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EinvoiceSettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.CompanyId).IsRequired();
        builder.Property(settings => settings.IsEinvoicingApplicable).IsRequired();

        builder.Property(settings => settings.Environment)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(settings => settings.EwayBillThresholdMinor).IsRequired();

        builder.Property(settings => settings.ProtectedClientId);
        builder.Property(settings => settings.ProtectedClientSecret);
        builder.Property(settings => settings.ProtectedUsername);
        builder.Property(settings => settings.ProtectedPassword);

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(settings => settings.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // One row per company.
        builder.HasIndex(settings => settings.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(settings => settings.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
