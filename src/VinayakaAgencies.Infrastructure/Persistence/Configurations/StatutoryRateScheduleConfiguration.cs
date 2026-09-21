using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="StatutoryRateSchedule"/> (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// The same shape as <c>TaxRateScheduleConfiguration</c>, because it is the same idea: §2 extends
/// non-negotiable #6 to PF and ESI rates and ceilings, so each is a dated row resolved by the
/// payroll date and never a constant.
/// </remarks>
internal sealed class StatutoryRateScheduleConfiguration
    : IEntityTypeConfiguration<StatutoryRateSchedule>
{
    public void Configure(EntityTypeBuilder<StatutoryRateSchedule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StatutoryRateSchedules");

        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();

        builder.Property(schedule => schedule.CompanyId).IsRequired();

        builder.Property(schedule => schedule.Statute)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(schedule => schedule.EmployeeRatePercentMinor).IsRequired();
        builder.Property(schedule => schedule.EmployerRatePercentMinor).IsRequired();
        builder.Property(schedule => schedule.WageCeilingMinor);
        builder.Property(schedule => schedule.EligibilityCeilingMinor);

        builder.Property(schedule => schedule.EffectiveFrom).IsRequired();
        builder.Property(schedule => schedule.EffectiveTo);

        builder.Property(schedule => schedule.CreatedAtUtc).IsRequired();
        builder.Property(schedule => schedule.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(schedule => schedule.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(schedule => new
        {
            schedule.CompanyId,
            schedule.Statute,
            schedule.EffectiveFrom,
        });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(schedule => schedule.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


