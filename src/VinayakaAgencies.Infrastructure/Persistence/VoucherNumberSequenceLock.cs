using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VinayakaAgencies.Application.Accounting;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// Locks the sequence row with a no-op <c>UPDATE</c> on the context's own connection.
/// </summary>
/// <remarks>
/// <b>Why an <c>UPDATE</c> that changes nothing.</b> It is the one way to take an exclusive row
/// lock that reads the same on both providers. The alternatives each fail on one of them: SQL
/// Server's <c>WITH (UPDLOCK, HOLDLOCK)</c> is a syntax error on SQLite, and a plain <c>SELECT</c>
/// takes only a shared lock, so two counters would both read the mark and then both try to promote
/// — which is the race this exists to remove. An <c>UPDATE</c> takes the write lock immediately and
/// holds it until the transaction commits, on SQLite and on SQL Server alike.
/// <para>
/// Setting the column to itself is deliberate rather than lazy: bumping the token here would make
/// the caller's own save look like somebody else's change and fail its own concurrency check.
/// </para>
/// <para>
/// <b>Why the SQL is a constant here rather than behind <c>ISqlDialect</c>.</b> ADR-0001 routes
/// <i>reporting</i> statements through the dialect so the Phase 9 provider switch is not a rewrite
/// of every report. This is not a report — it is a persistence primitive in the one layer that is
/// allowed to know the database exists, and the statement is identical on both providers, so a
/// dialect entry would add a seam with nothing on either side of it. If SQL Server ever needs a
/// hint here, that is the moment it moves behind the dialect.
/// </para>
/// </remarks>
internal sealed class VoucherNumberSequenceLock : IVoucherNumberSequenceLock
{
    private const string LockSql = """
        UPDATE VoucherNumberSequences
        SET ConcurrencyToken = ConcurrencyToken
        WHERE SeriesId = @SeriesId AND FinancialYearId = @FinancialYearId;
        """;

    private const string ReadSql = """
        SELECT LastAllocatedNumber
        FROM VoucherNumberSequences
        WHERE SeriesId = @SeriesId AND FinancialYearId = @FinancialYearId;
        """;

    private readonly AppDbContext _context;

    public VoucherNumberSequenceLock(AppDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<long?> LockAndReadAsync(
        Guid seriesId,
        Guid financialYearId,
        CancellationToken cancellationToken = default)
    {
        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException("SeriesId must not be empty.", nameof(seriesId));
        }

        if (financialYearId == Guid.Empty)
        {
            throw new ArgumentException("FinancialYearId must not be empty.", nameof(financialYearId));
        }

        var connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        // Enlisted in the caller's transaction, which is the whole point: a lock on any other
        // connection would be released before the voucher it is protecting had been written.
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        await using (var locking = CreateCommand(connection, transaction, LockSql, seriesId, financialYearId))
        {
            await locking.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var reading = CreateCommand(connection, transaction, ReadSql, seriesId, financialYearId);
        var stored = await reading.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        // No row yet: the first allocation of a financial year. The caller creates it.
        return stored is null or DBNull ? null : Convert.ToInt64(stored, provider: null);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        Guid seriesId,
        Guid financialYearId)
    {
        var command = connection.CreateCommand();

        try
        {
            command.CommandText = sql;
            command.Transaction = transaction;

            Add(command, "@SeriesId", seriesId);
            Add(command, "@FinancialYearId", financialYearId);

            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    /// <remarks>
    /// The <see cref="Guid"/> is bound as the provider's own parameter type rather than converted
    /// to text here. SQLite stores a GUID as TEXT and SQL Server as <c>uniqueidentifier</c>; writing
    /// the string by hand would work on one provider and silently match no rows on the other, which
    /// would read as "no sequence yet" and restart the series at 1 on a company with live numbers.
    /// </remarks>
    private static void Add(DbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();

        parameter.ParameterName = name;
        parameter.Value = value;

        command.Parameters.Add(parameter);
    }
}
