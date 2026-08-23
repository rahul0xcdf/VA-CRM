using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="FinancialYear"/> (spec §5, §12).
/// </summary>
/// <remarks>
/// <c>StartDate</c> and <c>EndDate</c> are <see cref="DateOnly"/>, not <see cref="DateTimeOffset"/>:
/// a financial year is a range in the local calendar (1 April – 31 March under decision D1), and
/// expressing 1 April as an instant would put a voucher entered late in the evening into the wrong
/// year. CLAUDE.md's date convention, applied without exception in this phase.
/// </remarks>
internal sealed class FinancialYearConfiguration : IEntityTypeConfiguration<FinancialYear>
{
    public void Configure(EntityTypeBuilder<FinancialYear> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("FinancialYears");

        builder.HasKey(year => year.Id);

        builder.Property(year => year.Id).ValueGeneratedNever();

        builder.Property(year => year.CompanyId).IsRequired();

        builder.Property(year => year.StartDate).IsRequired();
        builder.Property(year => year.EndDate).IsRequired();

        builder.Property(year => year.Label).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(year => year.IsClosed).IsRequired();

        builder.Property(year => year.CreatedAtUtc).IsRequired();
        builder.Property(year => year.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(year => year.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Years are created lazily on the first post into them (§12); the unique index is what stops
        // two concurrent lazy creations from producing a duplicate year for the same company.
        builder.HasIndex(year => new { year.CompanyId, year.StartDate }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(year => year.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
