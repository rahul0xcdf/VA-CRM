using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="VoucherNumberSeries"/> (spec §5, §7.2).
/// </summary>
/// <remarks>
/// Owner decision D2: the counter resets each financial year, gaps are blocked, and a manual
/// override is permitted and edit-logged. None of that is stored here — the series holds only the
/// format and the policy; the counter lives on <see cref="VoucherNumberSequence"/>, keyed by
/// financial year, so the reset is structural rather than a job someone has to run.
/// </remarks>
internal sealed class VoucherNumberSeriesConfiguration : IEntityTypeConfiguration<VoucherNumberSeries>
{
    public void Configure(EntityTypeBuilder<VoucherNumberSeries> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("VoucherNumberSeries");

        builder.HasKey(series => series.Id);

        builder.Property(series => series.Id).ValueGeneratedNever();

        builder.Property(series => series.CompanyId).IsRequired();
        builder.Property(series => series.VoucherTypeId).IsRequired();

        builder.Property(series => series.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(series => series.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        // Bounded so that prefix + padded digits + suffix cannot outgrow VoucherRevision.Number and
        // truncate at Phase 9 — a voucher number is printed on a customer's document and filed with
        // a return, so a silently shortened one is a compliance problem, not a display glitch.
        builder.Property(series => series.Prefix).IsRequired().HasMaxLength(ColumnLengths.NumberAffix);
        builder.Property(series => series.Suffix).HasMaxLength(ColumnLengths.NumberAffix);

        builder.Property(series => series.Width).IsRequired();
        builder.Property(series => series.StartNumber).IsRequired();

        builder.Property(series => series.ResetPolicy)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(series => series.AllowManualOverride).IsRequired();
        builder.Property(series => series.IsActive).IsRequired();

        builder.Property(series => series.CreatedAtUtc).IsRequired();
        builder.Property(series => series.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(series => series.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Scoped to the voucher type rather than to the company alone: a series called "Default"
        // under both Sales and Payment is normal, and rejecting it would be wrong (spec §5).
        builder.HasIndex(series => new { series.CompanyId, series.VoucherTypeId, series.NameNormalised })
            .IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(series => series.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoucherType>()
            .WithMany()
            .HasForeignKey(series => series.VoucherTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
