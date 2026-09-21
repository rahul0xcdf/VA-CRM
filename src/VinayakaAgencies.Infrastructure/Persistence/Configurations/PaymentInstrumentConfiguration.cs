using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Banking;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PaymentInstrument"/> (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// One instrument per voucher, enforced by a unique index: a voucher that settled twice is a data
/// error, and letting two rows exist would make the cheque register double-count a leaf.
/// <para>
/// <b>The cheque-number index is filtered, not unique.</b> Re-using a cheque number is wrong, but it
/// is wrong in a way the software must let the owner record — a cancelled leaf re-entered, or a
/// number keyed from a stub that turns out to be duplicated by the bank. The index exists so the
/// register can answer "which voucher used leaf 004512" instantly; refusing the second row outright
/// would block an entry the books genuinely need.
/// </para>
/// </remarks>
internal sealed class PaymentInstrumentConfiguration : IEntityTypeConfiguration<PaymentInstrument>
{
    public void Configure(EntityTypeBuilder<PaymentInstrument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PaymentInstruments");

        builder.HasKey(instrument => instrument.Id);
        builder.Property(instrument => instrument.Id).ValueGeneratedNever();

        builder.Property(instrument => instrument.CompanyId).IsRequired();
        builder.Property(instrument => instrument.VoucherId).IsRequired();
        builder.Property(instrument => instrument.BankLedgerId).IsRequired();

        builder.Property(instrument => instrument.Mode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(instrument => instrument.InstrumentNumber)
            .HasMaxLength(PaymentInstrument.ReferenceLength);
        builder.Property(instrument => instrument.InstrumentDate).IsRequired();
        builder.Property(instrument => instrument.BankReference)
            .HasMaxLength(PaymentInstrument.ReferenceLength);
        builder.Property(instrument => instrument.PayeeName).HasMaxLength(ColumnLengths.Name);
        builder.Property(instrument => instrument.Remarks).HasMaxLength(ColumnLengths.Narration);
        builder.Property(instrument => instrument.IsPrinted).IsRequired();
        builder.Property(instrument => instrument.PrintedAtUtc);

        builder.Property(instrument => instrument.CreatedAtUtc).IsRequired();
        builder.Property(instrument => instrument.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(instrument => instrument.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // A computed projection of Mode, never a column.
        builder.Ignore(instrument => instrument.IsPrintable);

        builder.HasIndex(instrument => instrument.VoucherId).IsUnique();

        // The cheque register: by bank account, in instrument-date order.
        builder.HasIndex(instrument => new
        {
            instrument.CompanyId,
            instrument.BankLedgerId,
            instrument.InstrumentDate,
        });

        // "Which voucher used this cheque leaf" — only rows that have a number.
        builder.HasIndex(instrument => new { instrument.CompanyId, instrument.InstrumentNumber })
            .HasFilter(IndexFilters.IsNotNull(nameof(PaymentInstrument.InstrumentNumber)));

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(instrument => instrument.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(instrument => instrument.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(instrument => instrument.BankLedgerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
