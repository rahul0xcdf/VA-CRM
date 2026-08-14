using MotorPumpSuite.Domain.Auditing;

namespace MotorPumpSuite.Application.Auditing;

/// <summary>One edit-log row as shown to the user.</summary>
public sealed record AuditLogRow(
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    string UserName,
    string MachineName,
    AuditAction Action,
    string EntityType,
    string EntityId,
    string ChangedProperties,
    string? BeforeJson,
    string? AfterJson);

/// <summary>Result of re-walking the log's hash chain.</summary>
/// <param name="FirstBrokenSequence">Sequence of the earliest row that failed, if any.</param>
public sealed record AuditChainVerification(
    long RowsChecked,
    bool IsIntact,
    long? FirstBrokenSequence,
    string? Detail);

/// <summary>
/// Read-only access to the edit log.
/// </summary>
/// <remarks>
/// There is deliberately no writer and no deleter on this interface, and none anywhere else: rows
/// are written only by the interceptor inside <c>SaveChangesAsync</c>, so the log cannot be
/// bypassed by forgetting to call something, and cannot be pruned by any user including an
/// administrator (project.md §4.1, MCA Rule 3(1)).
/// </remarks>
public interface IAuditLogReader
{
    Task<IReadOnlyList<AuditLogRow>> ListAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? entityType = null,
        int maxRows = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogRow>> ListForEntityAsync(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Re-walks the chain and reports the first row whose hash no longer verifies.</summary>
    Task<AuditChainVerification> VerifyChainAsync(CancellationToken cancellationToken = default);
}
