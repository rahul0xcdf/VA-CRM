using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TaxSection"/> (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// Uniqueness is soft in the same way <c>HsnSacConfiguration</c>'s is, and for a sharper reason: the
/// Income Tax Act 2025 renumbering retires the 194-series and introduces new codes from 1 April 2026,
/// so a code has to be retirable without deleting the rows a filed return was built from. The unique
/// index covers <em>active</em> rows only, and <see cref="TaxSection.Kind"/> is a key column because
/// a TDS section and a TCS section may legitimately share a number.
/// </remarks>
internal sealed class TaxSectionConfiguration : IEntityTypeConfiguration<TaxSection>
{
    public void Configure(EntityTypeBuilder<TaxSection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TaxSections");

        builder.HasKey(section => section.Id);
        builder.Property(section => section.Id).ValueGeneratedNever();

        builder.Property(section => section.CompanyId).IsRequired();

        // Enums by name, never by ordinal (ADR-0003 / spec §5): reordering the enum must not
        // silently change what a stored row means.
        builder.Property(section => section.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        // Sized to the domain's own 16-character bound — "206C(1H)" is the widest real code.
        builder.Property(section => section.Code).IsRequired().HasMaxLength(16);
        builder.Property(section => section.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(section => section.StatutePaymentCode).HasMaxLength(16);
        builder.Property(section => section.IsActive).IsRequired();

        builder.Property(section => section.CreatedAtUtc).IsRequired();
        builder.Property(section => section.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(section => section.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(section => new { section.CompanyId, section.Kind, section.Code })
            .IsUnique()
            .HasFilter(IndexFilters.IsTrue(nameof(TaxSection.IsActive)));

        builder.HasIndex(section => new { section.CompanyId, section.IsActive });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(section => section.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
