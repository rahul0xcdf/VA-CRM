using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CostCentre"/> (spec §5, §9 — decision D3).
/// </summary>
/// <remarks>
/// Schema only in Phase 1, and nests exactly the way <see cref="AccountGroup"/> does. Its
/// <c>MaterialisedPath</c> is capped at the same 400 characters for the same reason (§4.2): ten
/// levels of GUIDs, sized to stay inside SQL Server's 900-byte index key limit if and when Phase 2+
/// indexes it.
/// </remarks>
internal sealed class CostCentreConfiguration : IEntityTypeConfiguration<CostCentre>
{
    public void Configure(EntityTypeBuilder<CostCentre> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CostCentres");

        builder.HasKey(centre => centre.Id);

        builder.Property(centre => centre.Id).ValueGeneratedNever();

        builder.Property(centre => centre.CompanyId).IsRequired();

        builder.Property(centre => centre.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(centre => centre.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(centre => centre.MaterialisedPath)
            .IsRequired()
            .HasMaxLength(ColumnLengths.MaterialisedPath);

        builder.Property(centre => centre.CostCategoryId);
        builder.Property(centre => centre.ParentCostCentreId);

        builder.Property(centre => centre.IsActive).IsRequired();

        builder.Property(centre => centre.CreatedAtUtc).IsRequired();
        builder.Property(centre => centre.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(centre => centre.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(centre => centre.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCategory>()
            .WithMany()
            .HasForeignKey(centre => centre.CostCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCentre>()
            .WithMany()
            .HasForeignKey(centre => centre.ParentCostCentreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
