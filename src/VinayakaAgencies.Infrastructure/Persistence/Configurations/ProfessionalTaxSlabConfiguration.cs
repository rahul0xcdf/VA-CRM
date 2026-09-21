using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ProfessionalTaxSlab"/> (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// Indexed by <c>(CompanyId, EffectiveFrom, WageFromMinor)</c> so a month's slab table reads back
/// in band order — the order the first-match lookup in <c>PayrollMath.ProfessionalTax</c> depends
/// on being able to walk.
/// </remarks>
internal sealed class ProfessionalTaxSlabConfiguration : IEntityTypeConfiguration<ProfessionalTaxSlab>
{
    public void Configure(EntityTypeBuilder<ProfessionalTaxSlab> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ProfessionalTaxSlabs");

        builder.HasKey(slab => slab.Id);
        builder.Property(slab => slab.Id).ValueGeneratedNever();

        builder.Property(slab => slab.CompanyId).IsRequired();
        builder.Property(slab => slab.WageFromMinor).IsRequired();
        builder.Property(slab => slab.WageToMinor);
        builder.Property(slab => slab.TaxAmountMinor).IsRequired();
        builder.Property(slab => slab.EffectiveFrom).IsRequired();
        builder.Property(slab => slab.EffectiveTo);

        builder.Property(slab => slab.CreatedAtUtc).IsRequired();
        builder.Property(slab => slab.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(slab => slab.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(slab => new { slab.CompanyId, slab.EffectiveFrom, slab.WageFromMinor });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(slab => slab.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


