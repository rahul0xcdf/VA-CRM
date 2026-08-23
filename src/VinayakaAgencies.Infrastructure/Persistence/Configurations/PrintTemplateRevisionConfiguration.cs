using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Printing;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PrintTemplateRevision"/> — the append-only version history of a
/// print template (phase-3 §2).
/// </summary>
internal sealed class PrintTemplateRevisionConfiguration : IEntityTypeConfiguration<PrintTemplateRevision>
{
    public void Configure(EntityTypeBuilder<PrintTemplateRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PrintTemplateRevisions");

        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();

        builder.Property(revision => revision.CompanyId).IsRequired();
        builder.Property(revision => revision.TemplateId).IsRequired();
        builder.Property(revision => revision.Version).IsRequired();
        builder.Property(revision => revision.LayoutJson)
            .IsRequired()
            .HasMaxLength(ColumnLengths.TemplateLayoutJson);

        builder.Property(revision => revision.CreatedAtUtc).IsRequired();
        builder.Property(revision => revision.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        // One row per version; the template's pointer says which one prints.
        builder.HasIndex(revision => new { revision.TemplateId, revision.Version })
            .IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(revision => revision.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // History outlives its template: deleting a template must not sweep its versions away.
        builder.HasOne<PrintTemplate>()
            .WithMany()
            .HasForeignKey(revision => revision.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
