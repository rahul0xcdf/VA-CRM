using System.Data.Common;

namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// Opens a connection to the company database, hiding which provider is in use.
/// </summary>
/// <remarks>
/// <c>SqliteConnection</c> today, <c>SqlConnection</c> from Phase 9. No consumer names either type.
/// </remarks>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
