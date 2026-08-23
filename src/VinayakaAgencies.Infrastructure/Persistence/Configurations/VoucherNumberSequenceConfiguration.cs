using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="VoucherNumberSequence"/> (spec §5, §7.2).
/// </summary>
/// <remarks>
/// A high-water mark, one row per <c>(Series, FinancialYear)</c>. It carries no <c>CompanyId</c> of
/// its own: the company is reachable through either parent, and a third copy would be a third thing
/// that could disagree. The unique index is what makes reset-on-financial-year structural — a new
/// year gets a new row starting at zero, so there is no scheduled job and no "reset" button that
/// could be pressed on the wrong date.
/// </remarks>
internal sealed class VoucherNumberSequenceConfiguration : IEntityTypeConfiguration<VoucherNumberSequence>
{
    public void Configure(EntityTypeBuilder<VoucherNumberSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("VoucherNumberSequences");

        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.Id).ValueGeneratedNever();

        builder.Property(sequence => sequence.SeriesId).IsRequired();
        builder.Property(sequence => sequence.FinancialYearId).IsRequired();

        // Never decreases. Cancelling a voucher does not give its number back (D2): reuse would let
        // two documents share an invoice number, which is a GST problem, whereas a gap is merely
        // something to explain — and §8.7's gap report exists to explain it.
        builder.Property(sequence => sequence.LastAllocatedNumber).IsRequired();

        builder.HasIndex(sequence => new { sequence.SeriesId, sequence.FinancialYearId }).IsUnique();

        builder.HasOne<VoucherNumberSeries>()
            .WithMany()
            .HasForeignKey(sequence => sequence.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(sequence => sequence.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
