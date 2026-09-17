using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="EwayBill"/> (phase-6-einvoice-eway.md §2, §4a).
/// </summary>
/// <remarks>
/// <para>
/// <c>(CompanyId, TradeDocumentId)</c> is unique: one bill per consignment document at a time, which
/// is what the service relies on when it reuses a rejected or cancelled row rather than accumulating
/// drafts. <c>EwayBillNumber</c> is indexed but only unique among rows that have one — a draft with no
/// number yet is legitimate, and SQLite's filtered index gives that for free.
/// </para>
/// <para>
/// Part-A and Part-B share the table because the portal shares the concept; see the entity's remarks.
/// Every nullable Part-B column reflects that a bill genuinely exists before the vehicle is known.
/// </para>
/// </remarks>
internal sealed class EwayBillConfiguration : IEntityTypeConfiguration<EwayBill>
{
    public void Configure(EntityTypeBuilder<EwayBill> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EwayBills");

        builder.HasKey(bill => bill.Id);
        builder.Property(bill => bill.Id).ValueGeneratedNever();

        builder.Property(bill => bill.CompanyId).IsRequired();
        builder.Property(bill => bill.TradeDocumentId).IsRequired();

        builder.Property(bill => bill.Irn).HasMaxLength(64);

        // Minor units of 1/10,000 in an INTEGER column (ADR-0003).
        builder.Property(bill => bill.ConsignmentValueMinor).IsRequired();
        builder.Property(bill => bill.DistanceKilometres).IsRequired();

        builder.Property(bill => bill.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(bill => bill.TransportMode)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(bill => bill.VehicleType)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(bill => bill.EwayBillNumber);
        builder.Property(bill => bill.GeneratedAtUtc);
        builder.Property(bill => bill.ValidUntilUtc);

        builder.Property(bill => bill.TransporterGstin).HasMaxLength(15);
        builder.Property(bill => bill.TransporterName).HasMaxLength(ColumnLengths.Name);
        builder.Property(bill => bill.TransportDocumentNumber).HasMaxLength(ColumnLengths.VoucherNumber);
        builder.Property(bill => bill.TransportDocumentDate);
        builder.Property(bill => bill.VehicleNumber).HasMaxLength(20);

        builder.Property(bill => bill.CancelledAtUtc);
        builder.Property(bill => bill.CancellationReasonCode).HasMaxLength(8);
        builder.Property(bill => bill.CancellationRemarks).HasMaxLength(ColumnLengths.Narration);
        builder.Property(bill => bill.LastErrorText).HasMaxLength(ColumnLengths.Narration);

        builder.Property(bill => bill.CreatedAtUtc).IsRequired();
        builder.Property(bill => bill.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(bill => bill.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(bill => new { bill.CompanyId, bill.TradeDocumentId }).IsUnique();

        // Unique only where a number exists — drafts have none, and several may be in flight.
        builder.HasIndex(bill => new { bill.CompanyId, bill.EwayBillNumber })
            .IsUnique()
            .HasFilter("\"EwayBillNumber\" IS NOT NULL");

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(bill => bill.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TradeDocument>()
            .WithMany()
            .HasForeignKey(bill => bill.TradeDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
