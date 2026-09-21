using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TaxRateSchedule"/> (phase-7-tds-payroll-banking.md §2, C7.4).
/// </summary>
/// <remarks>
/// Mapped exactly as <c>GstRateScheduleConfiguration</c> is, because it is the same idea: a rate is
/// a dated row, the index on <c>(CompanyId, TaxSectionId, EffectiveFrom)</c> answers both "which row
/// covered this payment's date" and a history walk, and nothing cascades — a section is deactivated,
/// never deleted, so its rate history and the returns computed from it stay reconstructible.
/// <para>
/// Rates and both thresholds are INTEGER minor-unit columns. A threshold stored as a decimal on a
/// SQLite TEXT column is the defect the model guards exist to catch, and a threshold is exactly the
/// figure a comparison has to be exact about.
/// </para>
/// </remarks>
internal sealed class TaxRateScheduleConfiguration : IEntityTypeConfiguration<TaxRateSchedule>
{
    public void Configure(EntityTypeBuilder<TaxRateSchedule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TaxRateSchedules");

        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();

        builder.Property(schedule => schedule.CompanyId).IsRequired();
        builder.Property(schedule => schedule.TaxSectionId).IsRequired();

        builder.Property(schedule => schedule.PayeeCategory)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        // Percentages in minor units of 1/10,000 of a percent (ADR-0003): 1% is 10_000.
        builder.Property(schedule => schedule.RatePercentMinor).IsRequired();
        builder.Property(schedule => schedule.RateWithoutPanPercentMinor);

        builder.Property(schedule => schedule.SingleTransactionThresholdMinor).IsRequired();
        builder.Property(schedule => schedule.AnnualThresholdMinor).IsRequired();

        builder.Property(schedule => schedule.EffectiveFrom).IsRequired();
        builder.Property(schedule => schedule.EffectiveTo);

        builder.Property(schedule => schedule.CreatedAtUtc).IsRequired();
        builder.Property(schedule => schedule.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(schedule => schedule.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(schedule => new { schedule.CompanyId, schedule.EffectiveFrom });
        builder.HasIndex(schedule => new
        {
            schedule.CompanyId,
            schedule.TaxSectionId,
            schedule.EffectiveFrom,
        });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(schedule => schedule.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaxSection>()
            .WithMany()
            .HasForeignKey(schedule => schedule.TaxSectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
