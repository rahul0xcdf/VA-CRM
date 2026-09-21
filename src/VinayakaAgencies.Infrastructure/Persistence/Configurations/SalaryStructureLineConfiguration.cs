using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SalaryStructureLine"/> (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// Indexed by <c>(CompanyId, EmployeeId, EffectiveFrom)</c>, which is the read a payroll run makes
/// once per employee: "what was this person's structure in force at month end". Nothing cascades —
/// a structure line is the evidence for a pay slip that was issued.
/// </remarks>
internal sealed class SalaryStructureLineConfiguration : IEntityTypeConfiguration<SalaryStructureLine>
{
    public void Configure(EntityTypeBuilder<SalaryStructureLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SalaryStructureLines");

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.CompanyId).IsRequired();
        builder.Property(line => line.EmployeeId).IsRequired();
        builder.Property(line => line.PayHeadId).IsRequired();

        builder.Property(line => line.Basis)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        // Both nullable INTEGER minor-unit columns: which one means anything depends on the basis,
        // and a stale value in the other is a figure waiting to be used by mistake.
        builder.Property(line => line.AmountMinor);
        builder.Property(line => line.PercentMinor);

        builder.Property(line => line.EffectiveFrom).IsRequired();
        builder.Property(line => line.EffectiveTo);

        builder.Property(line => line.CreatedAtUtc).IsRequired();
        builder.Property(line => line.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(line => line.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(line => new { line.CompanyId, line.EmployeeId, line.EffectiveFrom });
        builder.HasIndex(line => line.PayHeadId);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(line => line.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(line => line.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PayHead>()
            .WithMany()
            .HasForeignKey(line => line.PayHeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


