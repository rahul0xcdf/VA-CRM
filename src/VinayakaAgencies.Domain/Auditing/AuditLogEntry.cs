using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VinayakaAgencies.Domain.Auditing;

/// <summary>
/// One immutable edit-log row: who changed what, when, and the before and after values.
/// </summary>
/// <remarks>
/// Required by <c>project.md</c> Â§4.1 and MCA Rule 3(1), in force since 1 April 2023. It cannot be
/// disabled by any user, including an administrator, and there is no delete path.
/// <para>
/// <b>This type deliberately does not derive from <c>Entity</c>.</b> <c>IRepository&lt;TEntity&gt;</c>
/// and <c>IUnitOfWork.Repository&lt;TEntity&gt;()</c> are both constrained to <c>Entity</c>, so no
/// caller can obtain a repository for this type and therefore no caller can reach
/// <c>Remove</c> or <c>Update</c> on it. The no-delete rule is enforced by the type system rather
/// than by discipline â€” a rule that depends on everyone remembering it is not a rule.
/// </para>
/// <para>
/// The identifier is a database-generated <see cref="Sequence"/> rather than a client GUID â€” the one
/// deliberate exception to ADR-0001's client-generated ids â€” because the hash chain needs a total
/// order that a client cannot choose, and a GUID gives no ordering at all.
/// </para>
/// </remarks>
public sealed class AuditLogEntry
{
    private AuditLogEntry()
    {
        // EF Core materialisation.
    }

    public AuditLogEntry(
        DateTimeOffset occurredAtUtc,
        string userName,
        string machineName,
        Guid saveBatchId,
        AuditAction action,
        string entityType,
        string entityId,
        string changedProperties,
        string? beforeJson,
        string? afterJson,
        Guid? companyId = null,
        string? aggregateType = null,
        string? aggregateId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        OccurredAtUtc = occurredAtUtc;
        UserName = userName;
        MachineName = machineName ?? string.Empty;
        SaveBatchId = saveBatchId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        ChangedProperties = changedProperties ?? string.Empty;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        CompanyId = companyId;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
    }

    /// <summary>Database-generated, monotonically increasing. Orders the hash chain.</summary>
    public long Sequence { get; private set; }

    public Guid? CompanyId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string MachineName { get; private set; } = string.Empty;

    /// <summary>Groups every row written by one <c>SaveChangesAsync</c>, so a voucher and its lines
    /// can be seen as the single action they were.</summary>
    public Guid SaveBatchId { get; private set; }

    public AuditAction Action { get; private set; }

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    /// <summary>The aggregate this row belongs to â€” a journal line's voucher, for instance â€” so the
    /// UI can show a voucher's history without knowing the schema.</summary>
    public string? AggregateType { get; private set; }

    public string? AggregateId { get; private set; }

    /// <summary>Comma-separated names of the properties that actually changed.</summary>
    public string ChangedProperties { get; private set; } = string.Empty;

    /// <summary>Full before-image. Null on a create.</summary>
    public string? BeforeJson { get; private set; }

    /// <summary>Full after-image. Null on a delete â€” the before-image is what matters there.</summary>
    public string? AfterJson { get; private set; }

    /// <summary><see cref="RowHash"/> of the preceding row, or the genesis value for the first.</summary>
    public string PreviousHash { get; private set; } = string.Empty;

    /// <summary>SHA-256 over this row's content and <see cref="PreviousHash"/>.</summary>
    public string RowHash { get; private set; } = string.Empty;

    /// <summary>Hash standing in for "no previous row".</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// ASCII unit separator, used between fields when hashing.
    /// </summary>
    /// <remarks>
    /// It cannot occur in a GUID, an ISO-8601 timestamp or JSON text, so two different rows cannot
    /// be made to hash identically by shifting content across a delimiter.
    /// </remarks>
    private const char FieldSeparator = '\u001F';

    /// <summary>
    /// Links this row to the one before it and seals it.
    /// </summary>
    /// <remarks>
    /// The chain makes the log tamper-<i>evident</i>, not tamper-proof. Through Phase 8 the database
    /// is a local file the operator can open with any SQLite tool, so a determined edit is always
    /// possible â€” what the chain guarantees is that such an edit cannot be made to go unnoticed,
    /// because every subsequent row's hash would stop verifying.
    /// </remarks>
    public void Seal(string previousHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousHash);

        PreviousHash = previousHash;
        RowHash = ComputeHash(previousHash);
    }

    /// <summary>Recomputes the hash and compares it to the stored one.</summary>
    public bool VerifyHash() =>
        string.Equals(RowHash, ComputeHash(PreviousHash), StringComparison.OrdinalIgnoreCase);

    private string ComputeHash(string previousHash)
    {
        // Field-separated with a character that cannot appear in a GUID or an ISO-8601 timestamp,
        // so two different rows cannot produce the same input string by shifting a delimiter.
        var payload = string.Join(
            FieldSeparator,
            previousHash,
            OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            UserName,
            MachineName,
            SaveBatchId.ToString("N"),
            ((int)Action).ToString(CultureInfo.InvariantCulture),
            EntityType,
            EntityId,
            AggregateType ?? string.Empty,
            AggregateId ?? string.Empty,
            ChangedProperties,
            BeforeJson ?? string.Empty,
            AfterJson ?? string.Empty);

        // Lower-case hex, as spec §11.5 documents. Convert.ToHexString returns upper-case, and
        // although VerifyHash compares case-insensitively, the stored form is what an auditor
        // reads and what any external re-verification would be written against.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
