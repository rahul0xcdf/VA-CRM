using System.Globalization;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Auditing;

namespace VinayakaAgencies.Application.Auditing;

/// <summary>
/// Read-only access to the edit log, backed by the reporting seam.
/// </summary>
/// <remarks>
/// There is deliberately no writer and no deleter here — rows are written only by the capture
/// inside <c>SaveChangesAsync</c> (spec §11.2), and this class re-verifies the §11.5 hash chain
/// through <see cref="AuditLogEntry.ComputeRowHash"/>, the same function that sealed the rows. A
/// reader that could write, or a verifier with its own copy of the hash formula, would each be a
/// second thing to keep honest; there is one of neither.
/// </remarks>
public sealed class AuditLogReader(
    ISqlQueryExecutor queryExecutor,
    ICompanyContext companyContext) : IAuditLogReader
{
    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditLogRow>> ListAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? entityType = null,
        int maxRows = 500,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(fromUtc, toUtc, entityType, entityId: null, maxRows, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditLogRow>> ListForEntityAsync(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(null, null, entityType, entityId, int.MaxValue, cancellationToken);

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyChainAsync(
        CancellationToken cancellationToken = default)
    {
        // From sequence zero: an audit that starts mid-chain would silently bless everything before it.
        var rows = await _queryExecutor.QueryAsync<ChainRowSql>(
            AccountingQueryKeys.EditLogChainVerification,
            new { FromSequence = 0L },
            cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return new AuditChainVerification(0, IsIntact: true, null, null);
        }

        var previousHash = rows[0].PreviousHash;

        foreach (var row in rows)
        {
            if (!string.Equals(row.PreviousHash, previousHash, StringComparison.OrdinalIgnoreCase))
            {
                return new AuditChainVerification(
                    RowsChecked: rows.Count,
                    IsIntact: false,
                    FirstBrokenSequence: row.Sequence,
                    Detail: $"Row {row.Sequence} links to {row.PreviousHash[..8]}… but the preceding " +
                            $"row's hash is {previousHash[..8]}…. The chain was broken before this row.");
            }

            var recomputed = AuditLogEntry.ComputeRowHash(
                row.PreviousHash,
                ParseTimestamp(row.OccurredAtUtc),
                row.UserName,
                row.MachineName,
                ToGuid(row.SaveBatchId),
                Enum.Parse<AuditAction>(row.Action, ignoreCase: false),
                row.EntityType,
                row.EntityId,
                row.AggregateType,
                row.AggregateId,
                row.ChangedProperties,
                row.BeforeJson,
                row.AfterJson);

            if (!string.Equals(recomputed, row.RowHash, StringComparison.OrdinalIgnoreCase))
            {
                return new AuditChainVerification(
                    RowsChecked: rows.Count,
                    IsIntact: false,
                    FirstBrokenSequence: row.Sequence,
                    Detail: $"Row {row.Sequence}'s stored hash no longer matches its content — " +
                            "the row was altered after it was sealed.");
            }

            previousHash = row.RowHash;
        }

        return new AuditChainVerification(rows.Count, IsIntact: true, null, null);
    }

    private async Task<IReadOnlyList<AuditLogRow>> ListCoreAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? entityType,
        string? entityId,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<AuditLogSqlRow>(
            AccountingQueryKeys.EditLogForPeriod,
            new
            {
                CompanyId = companyId,
                FromDate = fromUtc,
                ToDate = toUtc,
                EntityType = entityType,
                EntityId = entityId,
                MaxRows = maxRows,
            },
            cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(MapRow).ToList();
    }

    private static AuditLogRow MapRow(AuditLogSqlRow row) => new(
        row.Sequence,
        ParseTimestamp(row.OccurredAtUtc),
        row.UserName,
        row.MachineName,
        Enum.Parse<AuditAction>(row.Action, ignoreCase: false),
        row.EntityType,
        row.EntityId,
        row.ChangedProperties,
        row.BeforeJson,
        row.AfterJson);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Guid ToGuid(string value) => Guid.Parse(value);

    private sealed record AuditLogSqlRow(
        long Sequence,
        string OccurredAtUtc,
        string UserName,
        string MachineName,
        string Action,
        string EntityType,
        string EntityId,
        string ChangedProperties,
        string? BeforeJson,
        string? AfterJson);

    private sealed record ChainRowSql(
        long Sequence,
        string PreviousHash,
        string RowHash,
        string OccurredAtUtc,
        string UserName,
        string MachineName,
        string SaveBatchId,
        string Action,
        string EntityType,
        string EntityId,
        string? AggregateType,
        string? AggregateId,
        string ChangedProperties,
        string? BeforeJson,
        string? AfterJson);
}
