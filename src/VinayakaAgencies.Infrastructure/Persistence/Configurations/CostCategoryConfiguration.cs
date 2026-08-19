using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CostCategory"/> (spec §5, §9 — decision D3).
/// </summary>
/// <remarks>
/// Schema only. No UI is built in Phase 1, and checkpoint C1.13 asserts that, so "unused" stays true
/// rather than quietly becoming a half-built feature.
/// </remarks>
internal sealed class CostCategoryConfiguration : IEntityTypeConfiguration<CostCategory>
{
    public void Configure(EntityTypeBuilder<CostCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CostCategories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id).ValueGeneratedNever();

        builder.Property(category => category.CompanyId).IsRequired();

        builder.Property(category => category.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(category => category.NameNormalised)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(category => category.IsActive).IsRequired();

        builder.Property(category => category.CreatedAtUtc).IsRequired();
        builder.Property(category => category.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(category => category.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(category => new { category.CompanyId, category.NameNormalised })
            .IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(category => category.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
