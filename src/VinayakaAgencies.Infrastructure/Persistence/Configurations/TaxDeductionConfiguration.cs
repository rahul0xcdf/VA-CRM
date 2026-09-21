using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TaxDeduction"/> (phase-7-tds-payroll-banking.md §2, C7.3).
/// </summary>
/// <remarks>
/// <b>The cumulative index is the one that matters.</b>
/// <c>(CompanyId, LedgerId, TaxSectionId, FinancialYearId, DeductionDate)</c> answers the single
/// question every posting asks — "what has this payee already received under this section this
/// year" — and C7.3's catch-up arithmetic is wrong if that read is wrong. It is also a read that
/// happens on every TDS-bearing voucher, so it cannot be a table scan once a year of payments has
/// accumulated.
/// <para>
/// Every amount is an INTEGER minor-unit column and every FK is
/// <see cref="DeleteBehavior.Restrict"/>. A deduction is evidence for a filed return: cancelling the
/// voucher that made it reverses it through a new posting, and nothing deletes the row.
/// </para>
/// </remarks>
internal sealed class TaxDeductionConfiguration : IEntityTypeConfiguration<TaxDeduction>
{
    public void Configure(EntityTypeBuilder<TaxDeduction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TaxDeductions");

        builder.HasKey(deduction => deduction.Id);
        builder.Property(deduction => deduction.Id).ValueGeneratedNever();

        builder.Property(deduction => deduction.CompanyId).IsRequired();
        builder.Property(deduction => deduction.VoucherId).IsRequired();
        builder.Property(deduction => deduction.LedgerId).IsRequired();
        builder.Property(deduction => deduction.TaxSectionId).IsRequired();
        builder.Property(deduction => deduction.FinancialYearId).IsRequired();

        builder.Property(deduction => deduction.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(deduction => deduction.PayeeCategory)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        // The section code as it stood at deduction time — a snapshot, not a join.
        builder.Property(deduction => deduction.SectionCode).IsRequired().HasMaxLength(16);

        builder.Property(deduction => deduction.DeductionDate).IsRequired();
        builder.Property(deduction => deduction.BaseAmountMinor).IsRequired();
        builder.Property(deduction => deduction.TaxableBaseMinor).IsRequired();
        builder.Property(deduction => deduction.DeductedAmountMinor).IsRequired();
        builder.Property(deduction => deduction.AppliedRatePercentMinor).IsRequired();
        builder.Property(deduction => deduction.CumulativeBaseAfterMinor).IsRequired();
        builder.Property(deduction => deduction.IsCertificateApplied).IsRequired();
        builder.Property(deduction => deduction.IsThresholdCrossingPayment).IsRequired();
        builder.Property(deduction => deduction.PayeeHadPan).IsRequired();

        builder.Property(deduction => deduction.CertificateNumber)
            .HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(deduction => deduction.CreatedAtUtc).IsRequired();
        builder.Property(deduction => deduction.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);

        // The cumulative-threshold read (C7.3).
        builder.HasIndex(deduction => new
        {
            deduction.CompanyId,
            deduction.LedgerId,
            deduction.TaxSectionId,
            deduction.FinancialYearId,
            deduction.DeductionDate,
        });

        // The register and the challan report, which run by section over a date range.
        builder.HasIndex(deduction => new
        {
            deduction.CompanyId,
            deduction.TaxSectionId,
            deduction.DeductionDate,
        });

        // "What did this voucher deduct" — also the FK index for VoucherId.
        builder.HasIndex(deduction => deduction.VoucherId);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(deduction => deduction.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(deduction => deduction.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(deduction => deduction.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaxSection>()
            .WithMany()
            .HasForeignKey(deduction => deduction.TaxSectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(deduction => deduction.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
