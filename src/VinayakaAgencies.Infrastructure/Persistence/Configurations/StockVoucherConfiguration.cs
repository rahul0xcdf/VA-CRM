using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using AccountingLedger = VinayakaAgencies.Domain.Accounting.Ledger;
using AccountingVoucher = VinayakaAgencies.Domain.Accounting.Voucher;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="StockVoucher"/> (phase-2-inventory.md §1).
/// </summary>
internal sealed class StockVoucherConfiguration : IEntityTypeConfiguration<StockVoucher>
{
    public void Configure(EntityTypeBuilder<StockVoucher> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StockVouchers");

        builder.HasKey(voucher => voucher.Id);
        builder.Property(voucher => voucher.Id).ValueGeneratedNever();

        builder.Property(voucher => voucher.CompanyId).IsRequired();

        builder.Property(voucher => voucher.Purpose)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(voucher => voucher.VoucherDate).IsRequired();
        builder.Property(voucher => voucher.CounterLedgerId);
        builder.Property(voucher => voucher.Narration).HasMaxLength(ColumnLengths.Narration);

        builder.Property(voucher => voucher.PostedVoucherId);
        builder.Property(voucher => voucher.PostedNumber).HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(voucher => voucher.CreatedAtUtc).IsRequired();
        builder.Property(voucher => voucher.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(voucher => voucher.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(voucher => new { voucher.CompanyId, voucher.VoucherDate });
        builder.HasIndex(voucher => new { voucher.CompanyId, voucher.PostedVoucherId });

        // The collection is read through the aggregate; field access keeps callers from mutating
        // the list behind the AddLine guards.
        builder.HasMany(voucher => voucher.Lines)
            .WithOne()
            .HasForeignKey(line => line.StockVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(voucher => voucher.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(voucher => voucher.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict both ways: a posted stock voucher and the accounting voucher it produced stand
        // or fall together, and neither may be deleted out from under the other.
        builder.HasOne<AccountingVoucher>()
            .WithMany()
            .HasForeignKey(voucher => voucher.PostedVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountingLedger>()
            .WithMany()
            .HasForeignKey(voucher => voucher.CounterLedgerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
