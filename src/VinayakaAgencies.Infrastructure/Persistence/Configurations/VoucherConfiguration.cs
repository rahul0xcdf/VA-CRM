using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Voucher"/> (spec §5, ADR-0002).
/// </summary>
/// <remarks>
/// A thin, permanently immutable identity: four foreign keys and the audit stamps, nothing that can
/// change. Everything alterable lives on <see cref="VoucherRevision"/>, which is what makes the
/// journal append-only. This GUID is also the stable id Phase 8 exports to Tally — Tally's import
/// overwrites by GUID, so it must survive every alteration untouched, which is why it is
/// client-generated and never store-generated.
/// </remarks>
internal sealed class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Vouchers");

        builder.HasKey(voucher => voucher.Id);

        builder.Property(voucher => voucher.Id).ValueGeneratedNever();

        builder.Property(voucher => voucher.CompanyId).IsRequired();
        builder.Property(voucher => voucher.VoucherTypeId).IsRequired();
        builder.Property(voucher => voucher.SeriesId).IsRequired();
        builder.Property(voucher => voucher.FinancialYearId).IsRequired();

        builder.Property(voucher => voucher.CreatedAtUtc).IsRequired();
        builder.Property(voucher => voucher.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(voucher => voucher.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(voucher => voucher.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoucherType>()
            .WithMany()
            .HasForeignKey(voucher => voucher.VoucherTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoucherNumberSeries>()
            .WithMany()
            .HasForeignKey(voucher => voucher.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(voucher => voucher.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
