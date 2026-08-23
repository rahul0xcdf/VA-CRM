using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="JournalLine"/> (spec §5, §8.1, ADR-0002, ADR-0003).
/// </summary>
/// <remarks>
/// The table every reported number is derived from, and the one that must never acquire a delete
/// path. Every relationship touching it is <see cref="DeleteBehavior.Restrict"/>: the FK to
/// <c>Ledger</c> also gives §4.6's "hard delete only when the row has no journal line" a database
/// backstop instead of a validation someone has to remember.
/// <para>
/// <b>The amount is a signed <c>long</c> in minor units of 1/10,000</b> (ADR-0003, and its
/// implementation note). Positive is a debit, negative a credit — one column, so the balance
/// invariant is a single exact integer comparison and a row carrying both a debit and a credit is
/// structurally impossible. <c>Amount</c>, <c>DebitAmount</c> and <c>CreditAmount</c> are computed
/// <c>decimal</c> projections and are ignored below; if one of them were ever mapped it would land
/// on a SQLite <c>TEXT</c> column, and <c>SUM</c> over TEXT is evaluated in IEEE-754 double — the
/// precise defect ADR-0003 exists to prevent.
/// </para>
/// </remarks>
internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("JournalLines");

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.CompanyId).IsRequired();
        builder.Property(line => line.VoucherId).IsRequired();
        builder.Property(line => line.VoucherRevisionId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.LedgerId).IsRequired();
        builder.Property(line => line.AmountMinor).IsRequired();

        // Nullable and unused in Phase 1 by design (decision D3, §9). Shipping it empty now costs a
        // column of nulls; adding it later costs an ALTER on the largest table in the database,
        // which under SQLite means a full-table copy of the transaction history.
        builder.Property(line => line.CostCentreId);

        builder.Property(line => line.Narration).HasMaxLength(ColumnLengths.Narration);

        // Computed presentation projections, never columns — see the remarks on this class.
        builder.Ignore(line => line.Amount);
        builder.Ignore(line => line.DebitAmount);
        builder.Ignore(line => line.CreditAmount);
        builder.Ignore(line => line.IsDebit);
        builder.Ignore(line => line.IsCredit);

        // Every ledger balance, trial balance, P&L and balance sheet reads through this (§8.3).
        builder.HasIndex(line => new { line.CompanyId, line.LedgerId });

        // Reads a revision's lines back in the order they were entered. Also serves as the foreign
        // key index for VoucherRevisionId, which is its leading column.
        builder.HasIndex(line => new { line.VoucherRevisionId, line.LineNumber });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(line => line.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Direct to the voucher, mirroring the denormalised key columns on VoucherRevision (§5):
        // the edit log files every row under its voucher without a join, and the Restrict rule
        // keeps a delete path from reaching posted history through this column either.
        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(line => line.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The relationship to VoucherRevision is configured from the parent side, in
        // VoucherRevisionConfiguration, because the collection needs field access. Declaring it
        // again here would give EF a second, duplicate relationship on the same foreign key.
        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(line => line.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCentre>()
            .WithMany()
            .HasForeignKey(line => line.CostCentreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
