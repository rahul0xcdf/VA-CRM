using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// Records every command EF Core sends to the database, in order, with the transaction it was sent
/// on.
/// </summary>
/// <remarks>
/// Written for checkpoint C1.18 — "an alteration supersedes before it inserts". That criterion is
/// deliberately about the <i>emitted SQL</i> rather than the outcome: an EF Core upgrade that
/// reordered the two writes, or someone collapsing spec §8.2's two saves into one, would leave
/// every outcome-level assertion passing while the window in which a voucher number is claimed by
/// two live revisions opened up.
/// <para>
/// Registered as an <c>IInterceptor</c> in the application service provider, which EF Core 8
/// discovers on its own, so the production <c>AddInfrastructure</c> composition is used unchanged.
/// </para>
/// </remarks>
internal sealed class SqlCommandRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<RecordedCommand> _commands = new();

    /// <summary>Every command seen so far, in the order it was sent.</summary>
    public IReadOnlyList<RecordedCommand> Commands => _commands.ToArray();

    public void Clear() => _commands.Clear();

    /// <summary>The 0-based positions of commands whose text matches all the given fragments.</summary>
    public IReadOnlyList<int> PositionsMatching(params string[] fragments)
    {
        var commands = Commands;

        return Enumerable.Range(0, commands.Count)
            .Where(index => fragments.All(fragment =>
                commands[index].CommandText.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command) =>
        _commands.Enqueue(new RecordedCommand(
            command.CommandText,
            // The transaction object's identity, not its contents: two commands carrying the same
            // non-null id were sent on one transaction.
            command.Transaction is null ? null : command.Transaction.GetHashCode()));

    internal sealed record RecordedCommand(string CommandText, int? TransactionId);
}
