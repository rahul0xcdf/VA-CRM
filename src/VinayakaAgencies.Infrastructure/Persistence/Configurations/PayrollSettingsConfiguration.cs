using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PayrollSettings"/> (checkpoint C7.7).
/// </summary>
/// <remarks>
/// One row per company, like its GST and TDS siblings. The three flags are plain required booleans
/// with no default value expressed here: the domain constructor leaves them false, and letting the
/// database express a default as well would be two places stating one fact.
/// </remarks>
internal sealed class PayrollSettingsConfiguration : IEntityTypeConfiguration<PayrollSettings>
{
    public void Configure(EntityTypeBuilder<PayrollSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayrollSettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.CompanyId).IsRequired();
        builder.Property(settings => settings.SalaryPayableLedgerName)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(settings => settings.IsProvidentFundEnabled).IsRequired();
        builder.Property(settings => settings.IsEmployeeStateInsuranceEnabled).IsRequired();
        builder.Property(settings => settings.IsProfessionalTaxEnabled).IsRequired();

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(settings => settings.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Computed projections of the three flags, never columns.
        builder.Ignore(settings => settings.HasNoStatutesInForce);

        builder.HasIndex(settings => settings.CompanyId).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(settings => settings.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


