using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TaxPayeeProfile"/> (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// One profile per ledger, enforced by a unique index rather than by convention: two profiles for
/// one party would let a deduction resolve a different PAN or a different class of person depending
/// on which row a query happened to return first, and the 206AA uplift turns on exactly that fact.
/// The index is unfiltered — unlike the section master there is no deactivation here, because a
/// party that stops being deducted from carries
/// <see cref="TaxPayeeProfile.IsDeductionApplicable"/> false and keeps its PAN on record.
/// </remarks>
internal sealed class TaxPayeeProfileConfiguration : IEntityTypeConfiguration<TaxPayeeProfile>
{
    public void Configure(EntityTypeBuilder<TaxPayeeProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TaxPayeeProfiles");

        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();

        builder.Property(profile => profile.CompanyId).IsRequired();
        builder.Property(profile => profile.LedgerId).IsRequired();

        builder.Property(profile => profile.PayeeCategory)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(profile => profile.Pan).HasMaxLength(TaxPayeeProfile.PanLength);
        builder.Property(profile => profile.IsDeductionApplicable).IsRequired();

        // The buyer's written declaration that they deduct on purchases from us — the fact that
        // switches off collection on sales to them (C7.6).
        builder.Property(profile => profile.DeclaresDeductingOnPurchases).IsRequired();

        builder.Property(profile => profile.CreatedAtUtc).IsRequired();
        builder.Property(profile => profile.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(profile => profile.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(profile => new { profile.CompanyId, profile.LedgerId }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(profile => profile.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(profile => profile.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
