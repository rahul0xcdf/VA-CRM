using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="RolePermissionGrant"/>.
/// </summary>
internal sealed class RolePermissionGrantConfiguration
    : IEntityTypeConfiguration<RolePermissionGrant>
{
    public void Configure(EntityTypeBuilder<RolePermissionGrant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RolePermissionGrants");

        builder.HasKey(grant => grant.Id);

        builder.Property(grant => grant.Id).ValueGeneratedNever();

        builder.Property(grant => grant.RoleId).IsRequired();

        // By name, like every other enum in the schema: a stored grant must not change meaning
        // because a later release inserted a member into the middle of the enum. Here that would
        // mean an Operator silently acquiring the right to cancel vouchers.
        builder.Property(grant => grant.Permission)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(grant => grant.CreatedAtUtc).IsRequired();
        builder.Property(grant => grant.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        // One row per permission per role. AppRole.Grant already skips a duplicate, but a UI that
        // saved twice, or a future importer, would otherwise be able to double-grant — harmless
        // today and exactly the kind of thing a later "count the grants" query gets wrong.
        builder.HasIndex(grant => new { grant.RoleId, grant.Permission }).IsUnique();
    }
}
