using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TradeDocumentLine"/> (phase-3 §2).
/// </summary>
internal sealed class TradeDocumentLineConfiguration : IEntityTypeConfiguration<TradeDocumentLine>
{
    public void Configure(EntityTypeBuilder<TradeDocumentLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TradeDocumentLines");

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.CompanyId).IsRequired();
        builder.Property(line => line.TradeDocumentId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.ItemId).IsRequired();

        // Minor units of 1/10,000, never TEXT decimals (ADR-0003); the decimal projections below
        // are computed once in the domain and never columns.
        builder.Property(line => line.QuantityMinor).IsRequired();
        builder.Property(line => line.RateMinor).IsRequired();
        builder.Property(line => line.DiscountPercentMinor).IsRequired();

        // Pricing evidence (phase-4 §4a, checkpoint C4.2): what pricing auto-applied, whether the
        // operator moved off it, and which tier produced it. Stored by name like every enum.
        builder.Property(line => line.AppliedRateMinor).IsRequired();
        builder.Property(line => line.IsPriceOverridden).IsRequired();
        builder.Property(line => line.PriceSource)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        // Phase 5 tax snapshot (phase-5-gst-returns.md §2), written once at posting time: taxable
        // value, the resolved rate, and the four tax heads — all minor units (ADR-0003).
        builder.Property(line => line.TaxableValueMinor).IsRequired();
        builder.Property(line => line.GstRatePercentMinor).IsRequired();
        builder.Property(line => line.IgstMinor).IsRequired();
        builder.Property(line => line.CgstMinor).IsRequired();
        builder.Property(line => line.SgstMinor).IsRequired();
        builder.Property(line => line.CessMinor).IsRequired();

        builder.Ignore(line => line.Quantity);
        builder.Ignore(line => line.Rate);
        builder.Ignore(line => line.DiscountPercent);
        builder.Ignore(line => line.GrossValue);
        builder.Ignore(line => line.NetValue);
        builder.Ignore(line => line.ParsedSerials);

        builder.Property(line => line.BatchNumber).HasMaxLength(ColumnLengths.BatchNumber);
        builder.Property(line => line.SerialNumbersText).HasMaxLength(ColumnLengths.SerialList);

        builder.Property(line => line.CreatedAtUtc).IsRequired();
        builder.Property(line => line.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(line => new { line.CompanyId, line.TradeDocumentId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(line => line.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Godown>()
            .WithMany()
            .HasForeignKey(line => line.GodownId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
