using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="HsnSac"/> (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Uniqueness is deliberately soft: a filtered unique index over <c>(CompanyId, Code)</c> among
/// active rows only, so a code retired by notification can be re-created later without history
/// fights while two active rows for one code remain a database impossibility.
/// </remarks>
internal sealed class HsnSacConfiguration : IEntityTypeConfiguration<HsnSac>
{
    public void Configure(EntityTypeBuilder<HsnSac> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("HsnSac");

        builder.HasKey(code => code.Id);
        builder.Property(code => code.Id).ValueGeneratedNever();

        builder.Property(code => code.CompanyId).IsRequired();

        // HSN codes run to 8 digits; SAC to 6. Sized with headroom and indexed, hence bounded.
        builder.Property(code => code.Code).IsRequired().HasMaxLength(16);
        builder.Property(code => code.Description).HasMaxLength(ColumnLengths.Name);
        builder.Property(code => code.IsActive).IsRequired();

        builder.Property(code => code.CreatedAtUtc).IsRequired();
        builder.Property(code => code.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(code => code.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(code => new { code.CompanyId, code.Code })
            .IsUnique()
            .HasFilter(IndexFilters.IsTrue(nameof(HsnSac.IsActive)));

        builder.HasIndex(code => new { code.CompanyId, code.IsActive });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(code => code.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
