using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SerialNumber"/> (phase-2-inventory.md §2, C2.2/C2.3).
/// </summary>
/// <remarks>
/// The unique <c>(CompanyId, ItemId, ValueNormalised)</c> index is what makes a serial's identity
/// a database fact: the row is created on first receipt and lives forever, so "in stock exactly
/// once" and "the same serial cannot be sold twice" hold even against raw SQL.
/// </remarks>
internal sealed class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
{
    public void Configure(EntityTypeBuilder<SerialNumber> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SerialNumbers");

        builder.HasKey(serial => serial.Id);
        builder.Property(serial => serial.Id).ValueGeneratedNever();

        builder.Property(serial => serial.CompanyId).IsRequired();
        builder.Property(serial => serial.ItemId).IsRequired();

        builder.Property(serial => serial.Value).IsRequired().HasMaxLength(ColumnLengths.SerialValue);
        builder.Property(serial => serial.ValueNormalised).IsRequired().HasMaxLength(ColumnLengths.SerialValue);

        builder.Property(serial => serial.Status)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(serial => serial.CurrentGodownId).IsRequired();
        builder.Property(serial => serial.ReceivedOn).IsRequired();
        builder.Property(serial => serial.ReceiptLineId).IsRequired();
        builder.Property(serial => serial.SaleLineId);
        builder.Property(serial => serial.SoldOn);

        builder.Property(serial => serial.CreatedAtUtc).IsRequired();
        builder.Property(serial => serial.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(serial => new { serial.CompanyId, serial.ItemId, serial.ValueNormalised })
            .IsUnique();

        builder.HasIndex(serial => new { serial.CompanyId, serial.ItemId, serial.Status });
        builder.HasIndex(serial => serial.CurrentGodownId);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(serial => serial.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(serial => serial.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Godown>()
            .WithMany()
            .HasForeignKey(serial => serial.CurrentGodownId)
            .OnDelete(DeleteBehavior.Restrict);

        // History links, not navigation sugar: deleting a stock voucher line that received or sold
        // a unit would orphan the serial's traceability.
        builder.HasOne<StockVoucherLine>()
            .WithMany()
            .HasForeignKey(serial => serial.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockVoucherLine>()
            .WithMany()
            .HasForeignKey(serial => serial.SaleLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
