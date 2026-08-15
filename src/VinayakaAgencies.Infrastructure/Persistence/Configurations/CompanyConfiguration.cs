using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Company"/>.
/// </summary>
/// <remarks>
/// Column widths are declared explicitly rather than left to the provider default. SQLite ignores
/// them, but SQL Server does not, and Phase 9 should inherit a schema that was already sized —
/// discovering column widths during the provider migration is exactly the kind of avoidable work
/// ADR-0001 exists to prevent.
/// </remarks>
internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Companies");

        builder.HasKey(c => c.Id);

        // Client-generated: the id is assigned in the Entity constructor so that a record's GUID
        // survives a Phase 8 Tally export / re-import cycle. Never let the store generate it.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.LegalName).IsRequired().HasMaxLength(200);

        // 15 characters, fixed by the GST registration format.
        builder.Property(c => c.Gstin).HasMaxLength(15);

        builder.Property(c => c.StateCode).IsRequired().HasMaxLength(2);
        builder.Property(c => c.BaseCurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(c => c.FinancialYearStartMonth).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();

        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.CreatedBy).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(c => c.Name).IsUnique();

        // Filtered so that several companies may sit un-registered with a null GSTIN, while a real
        // GSTIN can only ever belong to one company in this install.
        //
        // The predicate goes through IndexFilters rather than being written out here: an index
        // filter is dialect SQL, and ADR-0001 keeps dialect SQL in one place so Phase 9 has one file
        // to edit. IndexFilters.IsNotNull emits the same text this line held before, so the model is
        // unchanged and the InitialCreate migration is not disturbed.
        builder.HasIndex(c => c.Gstin)
            .IsUnique()
            .HasFilter(IndexFilters.IsNotNull(nameof(Company.Gstin)));
    }
}
