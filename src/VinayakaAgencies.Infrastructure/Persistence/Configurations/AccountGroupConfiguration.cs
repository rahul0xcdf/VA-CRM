using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AccountGroup"/> (spec §4, §5).
/// </summary>
/// <remarks>
/// The structure is held twice by design (§4.2): <c>ParentGroupId</c> is authoritative and
/// <c>MaterialisedPath</c> is a rebuildable cache of the ancestor chain, indexed so a balance
/// roll-up is one range seek instead of a recursive CTE. There is no balance column here and never
/// will be (§8.3) — a group's balance is a <c>SUM</c> over the journal lines beneath it.
/// </remarks>
internal sealed class AccountGroupConfiguration : IEntityTypeConfiguration<AccountGroup>
{
    public void Configure(EntityTypeBuilder<AccountGroup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AccountGroups");

        builder.HasKey(group => group.Id);

        // Client-generated (ADR-0001 §5): the GUID must survive a Phase 8 Tally export / re-import.
        builder.Property(group => group.Id).ValueGeneratedNever();

        builder.Property(group => group.CompanyId).IsRequired();

        builder.Property(group => group.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(group => group.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(group => group.MaterialisedPath)
            .IsRequired()
            .HasMaxLength(ColumnLengths.MaterialisedPath);

        // Stored as its name, not its integer: reordering AccountNature later must not turn every
        // historical asset row into a liability (spec §5, §11.3).
        builder.Property(group => group.Nature)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(group => group.AffectsGrossProfit).IsRequired();
        builder.Property(group => group.IsPrimary).IsRequired();
        builder.Property(group => group.IsSystem).IsRequired();
        builder.Property(group => group.SortOrder).IsRequired();
        builder.Property(group => group.IsActive).IsRequired();

        builder.Property(group => group.CreatedAtUtc).IsRequired();
        builder.Property(group => group.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(group => group.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Company-scoped, and over the normalised name so that "Cash" and "cash" collide on both
        // providers rather than depending on the collation (§10).
        builder.HasIndex(group => new { group.CompanyId, group.ParentGroupId, group.NameNormalised })
            .IsUnique();

        // Supports the §4.2 roll-up range predicate: path >= @prefix AND path < @prefixUpperBound.
        builder.HasIndex(group => new { group.CompanyId, group.MaterialisedPath });

        // A real foreign key with no navigation property (spec §5): §10's cross-company rule belongs
        // in the database, and omitting the navigation keeps §11.3's "navigations are never
        // serialised" trivially true.
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(group => group.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, explicitly. EF defaults a required relationship to Cascade, and a cascade path
        // reaching the chart of accounts is a delete path for posted history (spec §5, ADR-0002).
        builder.HasOne<AccountGroup>()
            .WithMany()
            .HasForeignKey(group => group.ParentGroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
