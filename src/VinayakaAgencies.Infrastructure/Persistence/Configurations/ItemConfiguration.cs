using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Item"/> (phase-2-inventory.md §2 as amended on approval).
/// </summary>
/// <remarks>
/// Deliberately no quantity column of any kind: on-hand is derived from the unified stock ledger,
/// exactly as ledger balances are derived from journal lines. The motor/pump attribute columns of
/// the draft spec were dropped on approval (open-questions.md #5); HSN/SAC arrives in Phase 5 as
/// data-driven config, never hardcoded.
/// </remarks>
internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Items");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.CompanyId).IsRequired();

        builder.Property(item => item.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(item => item.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(item => item.Code).HasMaxLength(ColumnLengths.ItemCode);
        builder.Property(item => item.Barcode).HasMaxLength(ColumnLengths.ItemCode);

        builder.Property(item => item.BaseUnitId).IsRequired();

        builder.Property(item => item.AlternateUnitId);

        // Minor units (ADR-0003): INTEGER column; the decimal projection is computed.
        builder.Property(item => item.ConversionFactorMinor);
        builder.Ignore(item => item.ConversionFactor);

        builder.Property(item => item.IsSerialTracked).IsRequired();
        builder.Property(item => item.IsBatchTracked).IsRequired();
        builder.Property(item => item.IsKit).IsRequired();
        builder.Property(item => item.IsActive).IsRequired();

        // Minor units (ADR-0003): the reorder report compares against summed ledger quantities,
        // so the threshold must live in the same exact integer space.
        builder.Property(item => item.ReorderLevelMinor).IsRequired();
        builder.Ignore(item => item.ReorderLevel);

        // Minor units (ADR-0003): the Phase 4 default sale price — margin-on-MRP basis and final
        // price-resolution fallback. Nullable until the item is priced.
        builder.Property(item => item.DefaultSalePriceMinor);
        builder.Ignore(item => item.DefaultSalePrice);

        builder.Property(item => item.ValuationOverride)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(item => item.CreatedAtUtc).IsRequired();
        builder.Property(item => item.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(item => item.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Same uniqueness semantics as Ledger: normalised name scoped by company.
        builder.HasIndex(item => new { item.CompanyId, item.NameNormalised }).IsUnique();

        // Lookup paths for entry screens and the C2.6 search; non-unique where duplicates are an
        // operator warning rather than a database impossibility.
        builder.HasIndex(item => new { item.CompanyId, item.Code });
        builder.HasIndex(item => new { item.CompanyId, item.Barcode });
        builder.HasIndex(item => new { item.CompanyId, item.StockGroupId });
        builder.HasIndex(item => new { item.CompanyId, item.BaseUnitId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockGroup>()
            .WithMany()
            .HasForeignKey(item => item.StockGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict in both directions: a unit an item uses cannot be deleted while referenced
        // (§4.6's rule given a database backstop), and the base/alternate pair is two ordinary
        // references rather than one relationship with different delete rules.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(item => item.BaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(item => item.AlternateUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
