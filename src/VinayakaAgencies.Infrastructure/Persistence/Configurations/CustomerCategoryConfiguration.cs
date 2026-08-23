using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CustomerCategory"/> (phase-4-customer-pricing.md §2).
/// </summary>
internal sealed class CustomerCategoryConfiguration : IEntityTypeConfiguration<CustomerCategory>
{
    public void Configure(EntityTypeBuilder<CustomerCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CustomerCategories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();

        builder.Property(category => category.CompanyId).IsRequired();

        builder.Property(category => category.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(category => category.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(category => category.Description).HasMaxLength(ColumnLengths.Narration);

        builder.Property(category => category.IsActive).IsRequired();

        builder.Property(category => category.CreatedAtUtc).IsRequired();
        builder.Property(category => category.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(category => category.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Same uniqueness semantics as Ledger and Item: normalised name scoped by company, so
        // "Dealer" and "dealer" collide on both providers (NameNormaliser's collation note).
        builder.HasIndex(category => new { category.CompanyId, category.NameNormalised }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(category => category.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
