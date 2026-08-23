using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Printing;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PrintTemplate"/> (phase-3 §2, §4a; ADR-0004).
/// </summary>
/// <remarks>
/// <see cref="PrintTemplate.CurrentRevisionId"/> deliberately carries no foreign key — the same
/// ruling that lets a voucher type and its default series insert in one pass (spec §5): the
/// template row and its first revision are staged together, and an FK would make each insert fail
/// on the other. The pairing is enforced by the template service, which appends the revision and
/// moves the pointer in one transaction.
/// </remarks>
internal sealed class PrintTemplateConfiguration : IEntityTypeConfiguration<PrintTemplate>
{
    public void Configure(EntityTypeBuilder<PrintTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PrintTemplates");

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();

        builder.Property(template => template.CompanyId).IsRequired();

        builder.Property(template => template.DocumentKind)
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName)
            .IsRequired();

        builder.Property(template => template.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(template => template.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(template => template.Priority).IsRequired();
        builder.Property(template => template.CurrentVersion).IsRequired();
        builder.Property(template => template.CurrentRevisionId).IsRequired();

        // One template of a given name per kind per company, matching every other master here.
        builder.HasIndex(template => new { template.CompanyId, template.DocumentKind, template.NameNormalised })
            .IsUnique();

        // The default-selection rule (C3.6) reads this ordering constantly.
        builder.HasIndex(template => new { template.CompanyId, template.DocumentKind, template.Priority });

        builder.Property(template => template.CreatedAtUtc).IsRequired();
        builder.Property(template => template.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(template => template.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(template => template.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
