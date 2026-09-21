using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SalaryVoucherLine"/> — the frozen pay-slip line.
/// </summary>
internal sealed class SalaryVoucherLineConfiguration : IEntityTypeConfiguration<SalaryVoucherLine>
{
    public void Configure(EntityTypeBuilder<SalaryVoucherLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SalaryVoucherLines");

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.CompanyId).IsRequired();
        builder.Property(line => line.SalaryVoucherId).IsRequired();
        builder.Property(line => line.EmployeeId).IsRequired();
        builder.Property(line => line.PayHeadId).IsRequired();
        builder.Property(line => line.LedgerId).IsRequired();

        // The head's name as it stood when the slip was produced — a snapshot, not a join.
        builder.Property(line => line.PayHeadName).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(line => line.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(line => line.Statute)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(line => line.AmountMinor).IsRequired();
        builder.Property(line => line.SortOrder).IsRequired();

        builder.Property(line => line.CreatedAtUtc).IsRequired();
        builder.Property(line => line.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);

        builder.Ignore(line => line.AggregateType);
        builder.Ignore(line => line.AggregateId);

        // "This employee's pay slip for this run", which is how a slip is read back.
        builder.HasIndex(line => new { line.SalaryVoucherId, line.EmployeeId, line.SortOrder });

        // The PF/ESI/PT challan reports, which group by statute over a period.
        builder.HasIndex(line => new { line.CompanyId, line.Statute });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(line => line.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PayHead>()
            .WithMany()
            .HasForeignKey(line => line.PayHeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(line => line.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

