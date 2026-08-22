using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Godown"/> (phase-2-inventory.md §2).
/// </summary>
internal sealed class GodownConfiguration : IEntityTypeConfiguration<Godown>
{
    public void Configure(EntityTypeBuilder<Godown> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Godowns");

        builder.HasKey(godown => godown.Id);
        builder.Property(godown => godown.Id).ValueGeneratedNever();

        builder.Property(godown => godown.CompanyId).IsRequired();

        builder.Property(godown => godown.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(godown => godown.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(godown => godown.Address).HasMaxLength(ColumnLengths.Narration);
        builder.Property(godown => godown.IsSystem).IsRequired();
        builder.Property(godown => godown.IsActive).IsRequired();

        builder.Property(godown => godown.CreatedAtUtc).IsRequired();
        builder.Property(godown => godown.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(godown => godown.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(godown => new { godown.CompanyId, godown.NameNormalised }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(godown => godown.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
