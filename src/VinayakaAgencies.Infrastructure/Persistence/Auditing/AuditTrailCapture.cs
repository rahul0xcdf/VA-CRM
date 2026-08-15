using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Auditing;

/// <summary>
/// Turns a change-tracker snapshot into the sealed edit-log rows that describe it.
/// </summary>
/// <remarks>
/// <c>specs/phase-1-accounting-core.md</c> §11.1: every insert, update and delete of every mapped
/// entity, with the full before and after images. There is deliberately no opt-out — no attribute,
/// no allow-list, no configuration key — so an entity added in a later phase is audited because it
/// is mapped, not because someone remembered to register it.
/// <para>
/// The rows come back sealed into the hash chain in the order they must be inserted (§11.5). The
/// caller inserts them in that order; the chain's <c>PreviousHash</c> linkage assumes it.
/// </para>
/// </remarks>
internal static class AuditTrailCapture
{
    private const string CompanyIdPropertyName = "CompanyId";

    /// <summary>
    /// The change-tracker entries an edit-log row must be written for.
    /// </summary>
    /// <remarks>
    /// Audit rows themselves are excluded: they are appended by this very save and describe the
    /// change rather than being part of it. Any audit row in a state other than <c>Added</c> is a
    /// tampering attempt and is rejected before this method is reached (§11.4 item 2).
    /// </remarks>
    public static List<EntityEntry> Auditable(ChangeTracker changeTracker) =>
        changeTracker.Entries()
            .Where(entry => entry.Entity is not AuditLogEntry)
            .Where(entry => entry.State
                is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

    public static List<AuditLogEntry> Seal(
        IReadOnlyList<EntityEntry> entries,
        DateTimeOffset occurredAtUtc,
        string userName,
        string machineName,
        Guid saveBatchId,
        string previousHash)
    {
        var rows = new List<AuditLogEntry>(entries.Count);
        var chainHash = previousHash;

        foreach (var entry in entries)
        {
            var row = Describe(entry, occurredAtUtc, userName, machineName, saveBatchId);

            row.Seal(chainHash);
            chainHash = row.RowHash;

            rows.Add(row);
        }

        return rows;
    }

    private static AuditLogEntry Describe(
        EntityEntry entry,
        DateTimeOffset occurredAtUtc,
        string userName,
        string machineName,
        Guid saveBatchId)
    {
        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Create,
            EntityState.Modified => AuditAction.Update,
            EntityState.Deleted => AuditAction.Delete,
            _ => throw new InvalidOperationException(
                $"Entity state '{entry.State}' does not describe a change and must not be audited."),
        };

        var (aggregateType, aggregateId) = ResolveAggregate(entry);

        return new AuditLogEntry(
            occurredAtUtc: occurredAtUtc,
            userName: userName,
            machineName: machineName,
            saveBatchId: saveBatchId,
            action: action,
            entityType: entry.Metadata.ClrType.Name,
            entityId: ResolveEntityId(entry),
            changedProperties: ResolveChangedProperties(entry, action),
            beforeJson: action == AuditAction.Create ? null : CanonicalAuditJson.OriginalImage(entry),
            afterJson: action == AuditAction.Delete ? null : CanonicalAuditJson.CurrentImage(entry),
            companyId: ResolveCompanyId(entry),
            aggregateType: aggregateType,
            aggregateId: aggregateId);
    }

    /// <summary>
    /// The primary key, rendered the way the images render it.
    /// </summary>
    /// <remarks>
    /// Client-generated GUIDs (ADR-0001 §5) are what make this possible before the insert runs: the
    /// id is known at construction, so the log row can reference it without a second pass over the
    /// change tracker after <c>SaveChanges</c>.
    /// </remarks>
    private static string ResolveEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();

        if (key is null)
        {
            return string.Empty;
        }

        var values = key.Properties.Select(property =>
        {
            var propertyEntry = entry.Property(property.Name);

            // A deleted row's current values are already gone from the tracker's point of view.
            return CanonicalAuditJson.Scalar(entry.State == EntityState.Deleted
                ? propertyEntry.OriginalValue
                : propertyEntry.CurrentValue);
        });

        return string.Join('|', values);
    }

    /// <summary>
    /// Names of the properties this change actually touched.
    /// </summary>
    /// <remarks>
    /// An update lists only what moved, which is the question an auditor asks of an alteration. A
    /// create and a delete list every property, because the whole row arrived or the whole row went
    /// — an empty list there would read as "nothing changed", which is the opposite of the truth.
    /// </remarks>
    private static string ResolveChangedProperties(EntityEntry entry, AuditAction action)
    {
        var names = action == AuditAction.Update
            ? entry.Properties.Where(property => property.IsModified).Select(p => p.Metadata.Name)
            : entry.Properties.Select(p => p.Metadata.Name);

        return string.Join(',', names.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Which company's books this change belongs to (§10), where the entity says so.
    /// </summary>
    private static Guid? ResolveCompanyId(EntityEntry entry)
    {
        if (entry.Entity is Company company)
        {
            return company.Id;
        }

        var property = entry.Metadata.FindProperty(CompanyIdPropertyName);

        if (property is null
            || (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(Guid))
        {
            return null;
        }

        var value = entry.State == EntityState.Deleted
            ? entry.Property(CompanyIdPropertyName).OriginalValue
            : entry.Property(CompanyIdPropertyName).CurrentValue;

        return value as Guid?;
    }

    /// <summary>
    /// The aggregate whose history this row belongs under (§11.1), where it is determinable.
    /// </summary>
    /// <remarks>
    /// Two sources, both structural: an EF ownership, which the model states outright, and
    /// <see cref="IBelongsToAggregate"/>, which the entity states. Nothing is inferred from the
    /// shape of an entity's foreign keys — a journal line references its revision, its ledger, its
    /// cost centre and its company, and a rule that guessed among them would eventually file a
    /// change under the wrong voucher, which is worse than filing it under none.
    /// </remarks>
    private static (string? Type, string? Id) ResolveAggregate(EntityEntry entry)
    {
        if (entry.Entity is IBelongsToAggregate declared)
        {
            return (declared.AggregateType, declared.AggregateId);
        }

        var ownership = entry.Metadata.FindOwnership();

        if (ownership is null)
        {
            return (null, null);
        }

        var owner = ownership.PrincipalEntityType;
        var ownerKey = owner.FindPrimaryKey();

        if (ownerKey is null)
        {
            return (owner.ClrType.Name, null);
        }

        var ownerIdValues = ownership.Properties.Select(property =>
        {
            var propertyEntry = entry.Property(property.Name);

            return CanonicalAuditJson.Scalar(entry.State == EntityState.Deleted
                ? propertyEntry.OriginalValue
                : propertyEntry.CurrentValue);
        });

        return (owner.ClrType.Name, string.Join('|', ownerIdValues));
    }
}
