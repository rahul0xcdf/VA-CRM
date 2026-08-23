using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ItemKitComponent"/> (phase-2-inventory.md §2, basic BOM only).
/// </summary>
internal sealed class ItemKitComponentConfiguration : IEntityTypeConfiguration<ItemKitComponent>
{
    public void Configure(EntityTypeBuilder<ItemKitComponent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ItemKitComponents");

        builder.HasKey(component => component.Id);
        builder.Property(component => component.Id).ValueGeneratedNever();

        builder.Property(component => component.KitItemId).IsRequired();
        builder.Property(component => component.ComponentItemId).IsRequired();

        // Minor units (ADR-0003): INTEGER column, decimal projection computed in the domain.
        builder.Property(component => component.QuantityMinor).IsRequired();
        builder.Ignore(component => component.Quantity);

        builder.Property(component => component.CreatedAtUtc).IsRequired();
        builder.Property(component => component.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        // One component row per (kit, component) pair; SetKitComponentsAsync replaces wholesale.
        builder.HasIndex(component => new { component.KitItemId, component.ComponentItemId }).IsUnique();
        builder.HasIndex(component => component.ComponentItemId);

        // Restrict both ways, like every relationship in the model (§5): removing an item that
        // still lists components — or a kit that a component row points at — is refused here and
        // expressed as explicit service-level steps instead of silent cascades.
        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(component => component.KitItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(component => component.ComponentItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
