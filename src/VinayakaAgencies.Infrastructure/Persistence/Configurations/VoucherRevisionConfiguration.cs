using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="VoucherRevision"/> (spec §5, §7.2, §8.2, ADR-0002).
/// </summary>
/// <remarks>
/// The table that makes the journal append-only. An alteration writes a new revision and stamps
/// <c>SupersededByRevisionId</c> on the old one — the only column in the design that transitions
/// once, from null to a value, and never again. "Current" is therefore a predicate rather than a
/// flag, and the filtered unique index below is built over exactly that predicate.
/// <para>
/// <b>No stored totals.</b> <c>TotalDebit</c>, <c>TotalCredit</c>, <c>NetMinor</c> and
/// <c>IsBalanced</c> are computed from the lines and are explicitly ignored. §8.3 promises a
/// reviewer can grep the migration for a column named <c>*Balance*</c> or <c>*Total*</c> and find
/// none; an accidentally mapped total would break that promise silently, on the one table where a
/// stale total would be a wrong set of books.
/// </para>
/// </remarks>
internal sealed class VoucherRevisionConfiguration : IEntityTypeConfiguration<VoucherRevision>
{
    public void Configure(EntityTypeBuilder<VoucherRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("VoucherRevisions");

        builder.HasKey(revision => revision.Id);

        builder.Property(revision => revision.Id).ValueGeneratedNever();

        // CompanyId, VoucherTypeId, SeriesId and FinancialYearId are denormalised copies of the
        // parent Voucher's (spec §5). Neither provider can build a unique index across a join, so
        // without them the numbering index below is simply unbuildable. All four are immutable on
        // Voucher and are taken from it in the constructor, so the copies cannot drift.
        builder.Property(revision => revision.CompanyId).IsRequired();
        builder.Property(revision => revision.VoucherId).IsRequired();
        builder.Property(revision => revision.VoucherTypeId).IsRequired();
        builder.Property(revision => revision.SeriesId).IsRequired();
        builder.Property(revision => revision.FinancialYearId).IsRequired();

        builder.Property(revision => revision.RevisionNumber).IsRequired();

        builder.Property(revision => revision.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        // The business date: DateOnly, because financial-year logic follows the local calendar and a
        // DateTimeOffset here invites an off-by-one at midnight (§12).
        builder.Property(revision => revision.VoucherDate).IsRequired();
        builder.Property(revision => revision.ReferenceDate);

        builder.Property(revision => revision.Number)
            .IsRequired()
            .HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(revision => revision.NumberNormalised)
            .IsRequired()
            .HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(revision => revision.IsManualNumber).IsRequired();

        builder.Property(revision => revision.Narration).HasMaxLength(ColumnLengths.Narration);
        builder.Property(revision => revision.ReferenceNumber).HasMaxLength(ColumnLengths.Name);
        builder.Property(revision => revision.ChangeReason).HasMaxLength(ColumnLengths.Narration);

        // ICreationStamped only: a revision is never modified, so there is no Modified* pair to map.
        builder.Property(revision => revision.CreatedAtUtc).IsRequired();
        builder.Property(revision => revision.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        // Computed projections over the lines. Ignored explicitly so none can be picked up by a
        // convention change or a future EF version — see the remarks on this class.
        builder.Ignore(revision => revision.NetMinor);
        builder.Ignore(revision => revision.IsBalanced);
        builder.Ignore(revision => revision.IsCurrent);
        builder.Ignore(revision => revision.TotalDebit);
        builder.Ignore(revision => revision.TotalCredit);

        builder.HasIndex(revision => new { revision.VoucherId, revision.RevisionNumber }).IsUnique();

        // Voucher-number uniqueness (§7.2). Filtered to the current revision, because a superseded
        // revision keeps the number it was posted under and must not collide with its successor.
        // FinancialYearId is a mandatory key column, not an optional one: D2 resets the counter each
        // year, so PMT/0001 legitimately recurs and a company-wide key would reject the second
        // year's first payment.
        builder.HasIndex(revision => new
            {
                revision.CompanyId,
                revision.VoucherTypeId,
                revision.SeriesId,
                revision.FinancialYearId,
                revision.NumberNormalised,
            })
            .IsUnique()
            .HasFilter(IndexFilters.IsNull(nameof(VoucherRevision.SupersededByRevisionId)));

        // The lines are exposed as a read-only list over a private backing field, so EF must read
        // and write the field rather than the property — the property has no setter to write to.
        builder.HasMany(revision => revision.Lines)
            .WithOne()
            .HasForeignKey(line => line.VoucherRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(revision => revision.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(revision => revision.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade. EF would default this required relationship to Cascade, which puts
        // a cascade-delete path from Voucher through VoucherRevision to JournalLine — a delete path
        // for posted lines, which ADR-0002 forbids outright (spec §5).
        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(revision => revision.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoucherType>()
            .WithMany()
            .HasForeignKey(revision => revision.VoucherTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoucherNumberSeries>()
            .WithMany()
            .HasForeignKey(revision => revision.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(revision => revision.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);

        // SupersededByRevisionId is deliberately NOT a foreign key — the same class of exception
        // spec §5 already grants VoucherType.DefaultSeriesId, and for a sharper reason.
        //
        // An alteration (§8.2) inserts the successor revision, which carries the same voucher
        // number, and stamps the predecessor as superseded. With a foreign key here, EF must order
        // the insert of the successor BEFORE the update of the predecessor, because the update
        // references a row that has to exist first. At that instant both revisions have a null
        // SupersededByRevisionId and the same number, so the filtered unique index above rejects
        // the insert: "UNIQUE constraint failed". The reverse order is equally impossible, because
        // the update would then point at a row that does not exist yet. Neither provider supports
        // deferred constraint checking that would let both land at commit — SQL Server has none at
        // all — so with the foreign key in place there is no ordering in which ADR-0002's core
        // operation can be saved. Verified, not assumed: see
        // AccountingRoundTripTests.SaveChangesAsync_ForASupersededRevision_FreesTheNumberForItsSuccessor.
        //
        // Integrity is not lost, it moves: the column is written once by
        // VoucherRevision.SupersedeBy, which refuses to overwrite an existing value, and the id it
        // is given is the client-generated id of a revision created in the same unit of work.
        builder.Property(revision => revision.SupersededByRevisionId);
    }
}
