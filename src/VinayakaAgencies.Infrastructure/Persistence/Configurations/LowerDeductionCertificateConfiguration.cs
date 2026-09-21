using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="LowerDeductionCertificate"/> (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// The lookup index is <c>(CompanyId, LedgerId, TaxSectionId, ValidFrom)</c> because that is the
/// question posting asks: "does this party hold a certificate for this section covering this date".
/// Certificate numbers are deliberately <b>not</b> unique — the same number can be reissued for a
/// different section or a later year, and refusing the second one would block a real document for a
/// rule the statute does not impose.
/// </remarks>
internal sealed class LowerDeductionCertificateConfiguration
    : IEntityTypeConfiguration<LowerDeductionCertificate>
{
    public void Configure(EntityTypeBuilder<LowerDeductionCertificate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LowerDeductionCertificates");

        builder.HasKey(certificate => certificate.Id);
        builder.Property(certificate => certificate.Id).ValueGeneratedNever();

        builder.Property(certificate => certificate.CompanyId).IsRequired();
        builder.Property(certificate => certificate.LedgerId).IsRequired();
        builder.Property(certificate => certificate.TaxSectionId).IsRequired();

        builder.Property(certificate => certificate.CertificateNumber)
            .IsRequired()
            .HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(certificate => certificate.RatePercentMinor).IsRequired();
        builder.Property(certificate => certificate.CeilingAmountMinor);
        builder.Property(certificate => certificate.ValidFrom).IsRequired();
        builder.Property(certificate => certificate.ValidTo).IsRequired();
        builder.Property(certificate => certificate.IsActive).IsRequired();

        builder.Property(certificate => certificate.CreatedAtUtc).IsRequired();
        builder.Property(certificate => certificate.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(certificate => certificate.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(certificate => new
        {
            certificate.CompanyId,
            certificate.LedgerId,
            certificate.TaxSectionId,
            certificate.ValidFrom,
        });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(certificate => certificate.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(certificate => certificate.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaxSection>()
            .WithMany()
            .HasForeignKey(certificate => certificate.TaxSectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
