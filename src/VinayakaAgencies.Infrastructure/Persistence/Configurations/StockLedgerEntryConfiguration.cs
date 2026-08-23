using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="StockLedgerEntry"/> — the unified stock ledger
/// (phase-2-inventory.md §2, open-questions.md #1).
/// </summary>
/// <remarks>
/// Append-only by convention and by foreign keys: every relationship is Restrict, so no delete path
/// reaches posted history through any column. Quantities and value are scaled integers in minor
/// units of 1/10,000 (ADR-0003) — the decimal projections are computed, never columns, so every
/// report sums exact integers on SQLite exactly as it will on SQL Server.
/// </remarks>
internal sealed class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StockLedgerEntries");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.CompanyId).IsRequired();
        builder.Property(entry => entry.GlobalSequence).IsRequired();
        builder.Property(entry => entry.EntryDate).IsRequired();

        builder.Property(entry => entry.Kind)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(entry => entry.Direction)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(entry => entry.ItemId).IsRequired();
        builder.Property(entry => entry.GodownId).IsRequired();

        // Minor units of 1/10,000, never TEXT decimals: SUM() over these columns is exact integer
        // arithmetic on SQLite and native arithmetic on SQL Server (ADR-0003).
        builder.Property(entry => entry.QuantityInMinor).IsRequired();
        builder.Property(entry => entry.QuantityOutMinor).IsRequired();
        builder.Property(entry => entry.UnitCostMinor).IsRequired();
        builder.Property(entry => entry.ValueMinor).IsRequired();

        // Computed presentation projections, never columns — see the remarks on this class.
        builder.Ignore(entry => entry.QuantityIn);
        builder.Ignore(entry => entry.QuantityOut);
        builder.Ignore(entry => entry.SignedQuantity);

        builder.Property(entry => entry.StockVoucherId).IsRequired();
        builder.Property(entry => entry.StockVoucherLineId).IsRequired();
        builder.Property(entry => entry.BatchNumber).HasMaxLength(ColumnLengths.BatchNumber);

        builder.Property(entry => entry.CreatedAtUtc).IsRequired();
        builder.Property(entry => entry.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        // Valuation replay reads one item's entries in posting order.
        builder.HasIndex(entry => new { entry.CompanyId, entry.ItemId, entry.GlobalSequence });

        // On-hand by godown and the godown-filtered reports.
        builder.HasIndex(entry => new { entry.CompanyId, entry.GodownId, entry.ItemId });
        builder.HasIndex(entry => new { entry.CompanyId, entry.EntryDate });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(entry => entry.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(entry => entry.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Godown>()
            .WithMany()
            .HasForeignKey(entry => entry.GodownId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockVoucher>()
            .WithMany()
            .HasForeignKey(entry => entry.StockVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockVoucherLine>()
            .WithMany()
            .HasForeignKey(entry => entry.StockVoucherLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
