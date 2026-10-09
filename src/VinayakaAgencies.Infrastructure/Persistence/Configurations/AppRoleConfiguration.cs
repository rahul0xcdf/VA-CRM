using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AppRole"/> (phase-9-multiuser.md §4a D4).
/// </summary>
internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AppRoles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id).ValueGeneratedNever();

        builder.Property(role => role.CompanyId).IsRequired();

        builder.Property(role => role.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(role => role.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(role => role.Description).HasMaxLength(ColumnLengths.Narration);

        builder.Property(role => role.IsSystem).IsRequired();
        builder.Property(role => role.IsOwnerRole).IsRequired();

        builder.Property(role => role.CreatedAtUtc).IsRequired();
        builder.Property(role => role.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(role => role.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Indexed over the normalised form, not the display name: SQLite compares case-sensitively
        // by default and SQL Server does not, so indexing Name would let "Owner" and "owner" coexist
        // on SQLite and then break the Phase 9 migration with duplicates already in the data.
        builder.HasIndex(role => new { role.CompanyId, role.NameNormalised }).IsUnique();

        builder.HasMany(role => role.Grants)
            .WithOne()
            .HasForeignKey(grant => grant.RoleId)

            // Restrict, like every other relationship in the model — the no-cascades rule in
            // spec §5 is stated without exception, and a grant is not special enough to be the
            // first. UserAdminService therefore removes a role's grant rows explicitly before
            // the role itself, and ReplaceGrants hands back what it dropped so the caller can.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(role => role.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata
            .FindNavigation(nameof(AppRole.Grants))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
