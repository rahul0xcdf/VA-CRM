using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="GstRateSchedule"/> (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Rates are dated data rows, never constants: the index on <c>(CompanyId, HsnSacId, EffectiveFrom)</c>
/// serves both "which rate covered this date" lookups and history walks. No cascade anywhere — a
/// schedule points at its HSN/SAC row forever, which is why codes are deactivated rather than
/// deleted.
/// </remarks>
internal sealed class GstRateScheduleConfiguration : IEntityTypeConfiguration<GstRateSchedule>
{
    public void Configure(EntityTypeBuilder<GstRateSchedule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GstRateSchedules");

        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();

        builder.Property(schedule => schedule.CompanyId).IsRequired();
        builder.Property(schedule => schedule.HsnSacId).IsRequired();

        // Percentages in minor units of 1/10,000 (ADR-0003): 18% is 180_000, an INTEGER column.
        builder.Property(schedule => schedule.RatePercentMinor).IsRequired();
        builder.Property(schedule => schedule.CompensationCessPercentMinor).IsRequired();

        builder.Property(schedule => schedule.EffectiveFrom).IsRequired();
        builder.Property(schedule => schedule.EffectiveTo);

        builder.Property(schedule => schedule.CreatedAtUtc).IsRequired();
        builder.Property(schedule => schedule.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(schedule => schedule.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(schedule => new { schedule.CompanyId, schedule.EffectiveFrom });
        builder.HasIndex(schedule => new { schedule.CompanyId, schedule.HsnSacId, schedule.EffectiveFrom });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(schedule => schedule.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: deleting a code out from under its rate history would orphan every return that
        // was computed from it.
        builder.HasOne<HsnSac>()
            .WithMany()
            .HasForeignKey(schedule => schedule.HsnSacId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
