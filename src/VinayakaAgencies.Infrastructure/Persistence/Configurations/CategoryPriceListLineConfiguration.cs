using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CategoryPriceListLine"/> (phase-4-customer-pricing.md §2, §4a).
/// </summary>
/// <remarks>
/// One price per item per category — the unique <c>(CustomerCategoryId, ItemId)</c> index. The
/// basis flag and the two nullable values are the per-line data; which value applies is a domain
/// guard (<see cref="CategoryPriceListLine.SetPrice"/>), so a row with both or neither cannot exist.
/// </remarks>
internal sealed class CategoryPriceListLineConfiguration
    : IEntityTypeConfiguration<CategoryPriceListLine>
{
    public void Configure(EntityTypeBuilder<CategoryPriceListLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CategoryPriceListLines");

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.CompanyId).IsRequired();
        builder.Property(line => line.CustomerCategoryId).IsRequired();
        builder.Property(line => line.ItemId).IsRequired();

        builder.Property(line => line.Basis)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        // Minor units of 1/10,000, never TEXT decimals (ADR-0003); exactly one is set, chosen by
        // the basis. The decimal projections are computed once in the domain and never columns.
        builder.Property(line => line.FixedPriceMinor);
        builder.Property(line => line.MarginPercentMinor);
        builder.Ignore(line => line.FixedPrice);
        builder.Ignore(line => line.MarginPercent);

        builder.Property(line => line.CreatedAtUtc).IsRequired();
        builder.Property(line => line.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(line => line.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(line => new { line.CustomerCategoryId, line.ItemId }).IsUnique();
        builder.HasIndex(line => new { line.CompanyId, line.ItemId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(line => line.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: deleting a category must not silently take its prices with it — deactivate it,
        // or delete it only once its customers and lines are gone.
        builder.HasOne<CustomerCategory>()
            .WithMany()
            .HasForeignKey(line => line.CustomerCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
