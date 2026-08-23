using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Auditing;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the edit log (<c>specs/phase-1-accounting-core.md</c> §11).
/// </summary>
/// <remarks>
/// The one table in the schema whose key is store-generated. §11.4 explains why: the hash chain
/// needs a total order that a client cannot choose, and a GUID gives no ordering at all. Every other
/// table keeps ADR-0001's client-generated GUID.
/// </remarks>
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    /// <summary>SHA-256 rendered as lower-case hex.</summary>
    private const int HashLength = 64;

    internal const string TableName = "AuditLogEntries";

    internal const string BlockUpdateTriggerName = "TR_AuditLogEntries_BlockUpdate";

    internal const string BlockDeleteTriggerName = "TR_AuditLogEntries_BlockDelete";

    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The triggers are declared to the model, not just created in the migration. EF Core 8
        // needs to know a table carries triggers or its SQL Server save path emits
        // INSERT … OUTPUT INSERTED.Sequence, which SQL Server rejects on a table with enabled
        // triggers. The annotation is inert on SQLite, so it costs nothing today and Phase 9 does
        // not break (spec §13).
        builder.ToTable(TableName, table =>
        {
            table.HasTrigger(BlockUpdateTriggerName);
            table.HasTrigger(BlockDeleteTriggerName);
        });

        builder.HasKey(entry => entry.Sequence);

        // The deliberate exception to ADR-0001's client-generated ids — see the type's remarks.
        builder.Property(entry => entry.Sequence).ValueGeneratedOnAdd();

        // No foreign key to Companies, by design (spec §5): the log must outlive any company row it
        // describes, including the delete of that row.
        builder.Property(entry => entry.CompanyId);

        builder.Property(entry => entry.OccurredAtUtc).IsRequired();
        builder.Property(entry => entry.UserName).IsRequired().HasMaxLength(256);
        builder.Property(entry => entry.MachineName).IsRequired().HasMaxLength(256);
        builder.Property(entry => entry.SaveBatchId).IsRequired();

        // Stored as its name, never its integer, for the same reason §11.3 gives for the JSON
        // images: a later reordering of AuditAction must not silently change what a historical row
        // says happened.
        builder.Property(entry => entry.Action)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(entry => entry.EntityType).IsRequired().HasMaxLength(256);
        builder.Property(entry => entry.EntityId).IsRequired().HasMaxLength(64);
        builder.Property(entry => entry.AggregateType).HasMaxLength(256);
        builder.Property(entry => entry.AggregateId).HasMaxLength(64);

        builder.Property(entry => entry.PreviousHash)
            .IsRequired()
            .HasMaxLength(HashLength)
            .IsFixedLength();

        builder.Property(entry => entry.RowHash)
            .IsRequired()
            .HasMaxLength(HashLength)
            .IsFixedLength();

        // Unbounded, overriding the context-wide 512-character default for strings. §11.1 stores
        // full row images: at nvarchar(512) they would truncate at Phase 9 — silent audit-log data
        // loss on a table whose own triggers block any repair (spec §5, column-lengths note).
        Unbounded(builder, entry => entry.ChangedProperties);
        Unbounded(builder, entry => entry.BeforeJson);
        Unbounded(builder, entry => entry.AfterJson);

        builder.Property(entry => entry.ChangedProperties).IsRequired();

        // The edit-log viewer's own query (Accounting.EditLog.ForPeriod, §8.4).
        builder.HasIndex(entry => new { entry.CompanyId, entry.OccurredAtUtc });

        // "Show me everything that ever happened to this voucher" — the drill-down §11.1 exists for.
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId });
        builder.HasIndex(entry => new { entry.AggregateType, entry.AggregateId });

        // Groups the rows written by one SaveChanges back into the single action they were.
        builder.HasIndex(entry => entry.SaveBatchId);
    }

    private static void Unbounded<TProperty>(
        EntityTypeBuilder<AuditLogEntry> builder,
        System.Linq.Expressions.Expression<Func<AuditLogEntry, TProperty>> property) =>
        builder.Property(property).Metadata.SetMaxLength(null);
}
