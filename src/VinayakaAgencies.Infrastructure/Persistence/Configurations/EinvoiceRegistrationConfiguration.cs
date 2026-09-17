using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="EinvoiceRegistration"/> (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The unique index on <c>(CompanyId, Irn)</c> is the load-bearing part of this file. Checkpoint C6.5
/// says a retry after a timeout must not create a duplicate IRN, and the service is careful to make
/// that true — but a constraint is what makes it true even if a future edit is careless, or if two
/// operations race. Enforcing uniqueness in code alone would leave the guarantee resting on a code
/// path nobody re-reads.
/// </para>
/// <para>
/// <c>(CompanyId, TradeDocumentId)</c> is unique as well, because one invoice has one registration:
/// the same invoice registered twice under different IRNs would mean its number or date changed after
/// posting, which the journal engine does not allow.
/// </para>
/// <para>
/// No cascades. A registration outlives edits to everything around it — it is a record of what a
/// government portal was told, and deleting the invoice row does not untell it.
/// </para>
/// </remarks>
internal sealed class EinvoiceRegistrationConfiguration : IEntityTypeConfiguration<EinvoiceRegistration>
{
    public void Configure(EntityTypeBuilder<EinvoiceRegistration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EinvoiceRegistrations");

        builder.HasKey(registration => registration.Id);
        builder.Property(registration => registration.Id).ValueGeneratedNever();

        builder.Property(registration => registration.CompanyId).IsRequired();
        builder.Property(registration => registration.TradeDocumentId).IsRequired();

        builder.Property(registration => registration.SupplierGstin).IsRequired().HasMaxLength(15);

        // Enums by name, per the model guard: an ordinal would silently re-point every stored row if
        // a member were ever inserted above another.
        builder.Property(registration => registration.DocumentType)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(registration => registration.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(ColumnLengths.EnumName);

        builder.Property(registration => registration.DocumentNumber)
            .IsRequired()
            .HasMaxLength(ColumnLengths.VoucherNumber);

        builder.Property(registration => registration.DocumentDate).IsRequired();

        builder.Property(registration => registration.Irn).IsRequired().HasMaxLength(64);

        builder.Property(registration => registration.AcknowledgementNumber);
        builder.Property(registration => registration.AcknowledgedAtUtc);
        builder.Property(registration => registration.PortalAckDateText).HasMaxLength(32);

        // The signed artefacts are JWS blobs and run to several kilobytes; no length cap, because
        // truncating a signature is the same as losing it.
        builder.Property(registration => registration.SignedInvoiceJws);
        builder.Property(registration => registration.SignedQrCodeJws);

        builder.Property(registration => registration.CancelledAtUtc);
        builder.Property(registration => registration.CancellationReasonCode).HasMaxLength(8);
        builder.Property(registration => registration.CancellationRemarks).HasMaxLength(ColumnLengths.Narration);
        builder.Property(registration => registration.LastErrorText).HasMaxLength(ColumnLengths.Narration);
        builder.Property(registration => registration.AttemptCount).IsRequired();

        builder.Property(registration => registration.CreatedAtUtc).IsRequired();
        builder.Property(registration => registration.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(registration => registration.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // The idempotency guarantee, in the schema rather than only in the service.
        builder.HasIndex(registration => new { registration.CompanyId, registration.Irn }).IsUnique();
        builder.HasIndex(registration => new { registration.CompanyId, registration.TradeDocumentId }).IsUnique();
        builder.HasIndex(registration => new { registration.CompanyId, registration.DocumentDate });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(registration => registration.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TradeDocument>()
            .WithMany()
            .HasForeignKey(registration => registration.TradeDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
