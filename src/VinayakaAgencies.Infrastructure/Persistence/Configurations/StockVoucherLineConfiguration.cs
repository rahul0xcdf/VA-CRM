using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="StockVoucherLine"/> (phase-2-inventory.md §1).
/// </summary>
internal sealed class StockVoucherLineConfiguration : IEntityTypeConfiguration<StockVoucherLine>
{
    public void Configure(EntityTypeBuilder<StockVoucherLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StockVoucherLines");

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.CompanyId).IsRequired();
        builder.Property(line => line.StockVoucherId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();

        builder.Property(line => line.Direction)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(line => line.ItemId).IsRequired();
        builder.Property(line => line.GodownId).IsRequired();

        // Minor units (ADR-0003): the quantity is a scaled integer column; the decimal projection
        // is computed and never mapped.
        builder.Property(line => line.QuantityMinor).IsRequired();
        builder.Ignore(line => line.Quantity);

        builder.Property(line => line.IsSerialTracked).IsRequired();
        builder.Property(line => line.IsBatchTracked).IsRequired();

        // Zero until valued: receipt lines are written with their cost at construction, issue
        // lines receive the engine's costing before the ledger rows are appended.
        builder.Property(line => line.UnitCostMinor).IsRequired();

        builder.Property(line => line.BatchNumber).HasMaxLength(ColumnLengths.BatchNumber);

        builder.Property(line => line.CreatedAtUtc).IsRequired();
        builder.Property(line => line.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(line => new { line.StockVoucherId, line.LineNumber });
        builder.HasIndex(line => new { line.CompanyId, line.ItemId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(line => line.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configured from StockVoucherConfiguration's side (the collection needs field access);
        // declaring it again here would create a duplicate relationship on the same foreign key.

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
