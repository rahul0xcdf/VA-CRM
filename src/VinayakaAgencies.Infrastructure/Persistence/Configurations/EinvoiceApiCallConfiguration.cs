using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="EinvoiceApiCall"/> (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// Append-only by intent and by shape: one row per attempt, no unique index that would stop a second
/// attempt being recorded, and no cascade that would let a tidy-up erase the history. The bodies are
/// unbounded text because they arrive already redacted and truncating them would cost the diagnostic
/// value the rows exist for.
/// </remarks>
internal sealed class EinvoiceApiCallConfiguration : IEntityTypeConfiguration<EinvoiceApiCall>
{
    public void Configure(EntityTypeBuilder<EinvoiceApiCall> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EinvoiceApiCalls");

        builder.HasKey(call => call.Id);
        builder.Property(call => call.Id).ValueGeneratedNever();

        builder.Property(call => call.CompanyId).IsRequired();
        builder.Property(call => call.EinvoiceRegistrationId).IsRequired();

        builder.Property(call => call.Operation).IsRequired().HasMaxLength(64);
        builder.Property(call => call.Endpoint).IsRequired().HasMaxLength(400);

        builder.Property(call => call.RedactedRequestBody).IsRequired();
        builder.Property(call => call.RedactedResponseBody);

        builder.Property(call => call.HttpStatusCode);
        builder.Property(call => call.PortalErrorCode).HasMaxLength(64);
        builder.Property(call => call.StartedAtUtc).IsRequired();
        builder.Property(call => call.CompletedAtUtc);
        builder.Property(call => call.TimedOut).IsRequired();

        builder.Property(call => call.CreatedAtUtc).IsRequired();
        builder.Property(call => call.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(call => new { call.CompanyId, call.EinvoiceRegistrationId, call.StartedAtUtc });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(call => call.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EinvoiceRegistration>()
            .WithMany()
            .HasForeignKey(call => call.EinvoiceRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
