using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TradeDocument"/>
/// (phase-3-sales-purchase-pipeline.md §2, §4a).
/// </summary>
/// <remarks>
/// The unique filtered index over <c>(CompanyId, Kind, Number)</c> is what makes a document's
/// number a database fact for the non-posting kinds — quotations and orders never reach a voucher
/// revision, so the journal engine's numbering index cannot protect them. The self-referencing
/// conversion link is Restrict both ways: a document that was carried forward cannot be deleted
/// out from under its successor.
/// </remarks>
internal sealed class TradeDocumentConfiguration : IEntityTypeConfiguration<TradeDocument>
{
    public void Configure(EntityTypeBuilder<TradeDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TradeDocuments");

        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();

        builder.Property(document => document.CompanyId).IsRequired();
        builder.Property(document => document.PartyLedgerId).IsRequired();

        builder.Property(document => document.Kind)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(document => document.Status)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(document => document.DocumentDate).IsRequired();

        builder.Property(document => document.Narration).HasMaxLength(ColumnLengths.Narration);
        builder.Property(document => document.PartyReferenceNumber).HasMaxLength(ColumnLengths.Name);

        // 15 characters, fixed by the GST registration format — same as Company.Gstin.
        builder.Property(document => document.PartyGstin).HasMaxLength(15);

        // Phase 5 tax snapshot (phase-5-gst-returns.md §2): written once at posting time.
        builder.Property(document => document.PlaceOfSupplyStateCode).HasMaxLength(2);
        builder.Property(document => document.IsReverseCharge).IsRequired();

        builder.Property(document => document.Number).HasMaxLength(ColumnLengths.VoucherNumber);
        builder.Property(document => document.ConvertedFromNumber).HasMaxLength(ColumnLengths.VoucherNumber);
        builder.Property(document => document.PostedNumber).HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(document => document.CreatedAtUtc).IsRequired();
        builder.Property(document => document.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(document => document.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Quotation/order numbers are claimed from series exactly like voucher numbers; this index
        // is their numbering guarantee, filtered because drafts (invoices) carry no number yet.
        builder.HasIndex(document => new { document.CompanyId, document.Kind, document.Number })
            .IsUnique()
            .HasFilter(IndexFilters.IsNotNull(nameof(TradeDocument.Number)));

        builder.HasIndex(document => new { document.CompanyId, document.DocumentDate });
        builder.HasIndex(document => new { document.CompanyId, document.PostedVoucherId });
        builder.HasIndex(document => new { document.CompanyId, document.ConvertedFromDocumentId });

        // The collection is read through the aggregate; field access keeps callers from mutating
        // the list behind the AddLine guards.
        builder.HasMany(document => document.Lines)
            .WithOne()
            .HasForeignKey(line => line.TradeDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(document => document.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(document => document.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(document => document.PartyLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Conversion drill-down: the successor names its source; deleting the source while a
        // successor stands is refused, not silently orphaned.
        builder.HasOne<TradeDocument>()
            .WithMany()
            .HasForeignKey(document => document.ConvertedFromDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(document => document.PostedVoucherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
