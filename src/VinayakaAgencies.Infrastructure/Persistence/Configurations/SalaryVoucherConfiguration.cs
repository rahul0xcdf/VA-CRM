using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SalaryVoucher"/> and its lines
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// Mirrors <c>StockVoucherConfiguration</c>: the aggregate owns its lines through the backing
/// field, and the totals are computed projections rather than stored columns — non-negotiable #4
/// applies to a payroll total exactly as it does to a ledger balance.
/// </remarks>
internal sealed class SalaryVoucherConfiguration : IEntityTypeConfiguration<SalaryVoucher>
{
    public void Configure(EntityTypeBuilder<SalaryVoucher> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SalaryVouchers");

        builder.HasKey(voucher => voucher.Id);
        builder.Property(voucher => voucher.Id).ValueGeneratedNever();

        builder.Property(voucher => voucher.CompanyId).IsRequired();
        builder.Property(voucher => voucher.PeriodStart).IsRequired();
        builder.Property(voucher => voucher.PeriodEnd).IsRequired();
        builder.Property(voucher => voucher.Narration).HasMaxLength(ColumnLengths.Narration);
        builder.Property(voucher => voucher.PostedVoucherId).IsRequired();
        builder.Property(voucher => voucher.PostedVoucherNumber)
            .IsRequired()
            .HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(voucher => voucher.CreatedAtUtc).IsRequired();
        builder.Property(voucher => voucher.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(voucher => voucher.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Derived from the lines, never stored (non-negotiable #4).
        builder.Ignore(voucher => voucher.GrossEarningsMinor);
        builder.Ignore(voucher => voucher.TotalDeductionsMinor);
        builder.Ignore(voucher => voucher.EmployerContributionsMinor);
        builder.Ignore(voucher => voucher.NetPayableMinor);
        builder.Ignore(voucher => voucher.EmployeeCount);

        // One run per month, so a second run for a month already paid is a database impossibility
        // rather than a duplicate set of challans nobody reconciles.
        builder.HasIndex(voucher => new { voucher.CompanyId, voucher.PeriodEnd }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(voucher => voucher.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(voucher => voucher.PostedVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(voucher => voucher.Lines)
            .WithOne()
            .HasForeignKey(line => line.SalaryVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata
            .FindNavigation(nameof(SalaryVoucher.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}


