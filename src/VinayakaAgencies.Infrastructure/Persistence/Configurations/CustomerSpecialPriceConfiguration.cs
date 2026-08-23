using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CustomerSpecialPrice"/>
/// (phase-4-customer-pricing.md §2 as added at approval, §4a).
/// </summary>
/// <remarks>
/// Same shape and uniqueness as the category price list, keyed by customer instead: at most one
/// special price per item per customer, which wins over the category line when present.
/// </remarks>
internal sealed class CustomerSpecialPriceConfiguration : IEntityTypeConfiguration<CustomerSpecialPrice>
{
    public void Configure(EntityTypeBuilder<CustomerSpecialPrice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CustomerSpecialPrices");

        builder.HasKey(specialPrice => specialPrice.Id);
        builder.Property(specialPrice => specialPrice.Id).ValueGeneratedNever();

        builder.Property(specialPrice => specialPrice.CompanyId).IsRequired();
        builder.Property(specialPrice => specialPrice.CustomerId).IsRequired();
        builder.Property(specialPrice => specialPrice.ItemId).IsRequired();

        builder.Property(specialPrice => specialPrice.Basis)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        // Minor units of 1/10,000, never TEXT decimals (ADR-0003); exactly one is set, chosen by
        // the basis. The decimal projections are computed once in the domain and never columns.
        builder.Property(specialPrice => specialPrice.FixedPriceMinor);
        builder.Property(specialPrice => specialPrice.MarginPercentMinor);
        builder.Ignore(specialPrice => specialPrice.FixedPrice);
        builder.Ignore(specialPrice => specialPrice.MarginPercent);

        builder.Property(specialPrice => specialPrice.CreatedAtUtc).IsRequired();
        builder.Property(specialPrice => specialPrice.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(specialPrice => specialPrice.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(specialPrice => new { specialPrice.CustomerId, specialPrice.ItemId })
            .IsUnique();
        builder.HasIndex(specialPrice => new { specialPrice.CompanyId, specialPrice.ItemId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(specialPrice => specialPrice.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(specialPrice => specialPrice.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(specialPrice => specialPrice.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
