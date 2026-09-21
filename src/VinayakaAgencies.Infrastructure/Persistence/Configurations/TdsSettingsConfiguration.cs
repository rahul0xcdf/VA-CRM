using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TdsSettings"/> (phase-7-tds-payroll-banking.md §4a D2).
/// </summary>
/// <remarks>
/// One row per company, enforced by a unique index exactly as <c>GstSettingsConfiguration</c> does.
/// The two section ids are nullable and <see cref="DeleteBehavior.Restrict"/>: a section the owner
/// has pointed the purchase-of-goods or sale-of-goods flag at cannot be deleted out from under it,
/// which would leave a flag on and resolving to nothing — the failure mode
/// <c>TdsSettings.ConfigurePurchaseOfGoods</c> refuses at the domain level.
/// </remarks>
internal sealed class TdsSettingsConfiguration : IEntityTypeConfiguration<TdsSettings>
{
    public void Configure(EntityTypeBuilder<TdsSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TdsSettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.CompanyId).IsRequired();

        builder.Property(settings => settings.TdsPayableLedgerName)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);
        builder.Property(settings => settings.TcsPayableLedgerName)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(settings => settings.IsPurchaseOfGoodsDeductionEnabled).IsRequired();
        builder.Property(settings => settings.PurchaseOfGoodsSectionId);
        builder.Property(settings => settings.IsSaleOfGoodsCollectionEnabled).IsRequired();
        builder.Property(settings => settings.SaleOfGoodsSectionId);

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(settings => settings.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(settings => settings.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(settings => settings.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaxSection>()
            .WithMany()
            .HasForeignKey(settings => settings.PurchaseOfGoodsSectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaxSection>()
            .WithMany()
            .HasForeignKey(settings => settings.SaleOfGoodsSectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
